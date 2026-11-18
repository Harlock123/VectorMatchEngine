using System.Buffers.Binary;

namespace VectorMatchEngine.Core.Models;

/// <summary>
/// A mostly-zero vector kept as its non-zero entries, indices ascending. A field's vector has a few dozen non-zero
/// buckets out of 512, so this is what each field of a record is stored as (see <see cref="FieldVectorCodec"/>).
/// </summary>
public sealed class SparseVector
{
    public static SparseVector Empty { get; } = new(Array.Empty<ushort>(), Array.Empty<float>());

    public SparseVector(ushort[] indices, float[] values)
    {
        if (indices.Length != values.Length)
            throw new ArgumentException("A sparse vector needs as many values as indices.");
        Indices = indices;
        Values = values;
    }

    public ushort[] Indices { get; }
    public float[] Values { get; }

    /// <summary>A blank field: it has no trigrams, so it says nothing either way about a match.</summary>
    public bool IsEmpty => Indices.Length == 0;

    public static SparseVector FromDense(float[] dense)
    {
        int count = 0;
        foreach (var value in dense)
            if (value != 0)
                count++;
        if (count == 0)
            return Empty;
        var indices = new ushort[count];
        var values = new float[count];
        int k = 0;
        for (int d = 0; d < dense.Length; d++)
            if (dense[d] != 0)
            {
                indices[k] = (ushort)d;
                values[k++] = dense[d];
            }
        return new SparseVector(indices, values);
    }

    /// <summary>The dot product (the cosine, both being L2-normalized), clamped to [0, 1].</summary>
    public double Dot(SparseVector other)
    {
        float dot = 0;
        int i = 0, j = 0;
        var a = Indices;
        var b = other.Indices;
        while (i < a.Length && j < b.Length)
        {
            if (a[i] == b[j]) dot += Values[i++] * other.Values[j++];
            else if (a[i] < b[j]) i++;
            else j++;
        }
        return float.IsFinite(dot) ? Math.Clamp(dot, 0d, 1d) : 0d;
    }

    /// <summary>
    /// A 64-bit hash of the exact entries: two fields with the same cleaned value (or, encoded by sound, the same
    /// codes) have the same vector and so the same fingerprint. Blocking groups records by it.
    /// </summary>
    public ulong Fingerprint()
    {
        const ulong offset = 14695981039346656037UL, prime = 1099511628211UL;   // FNV-1a
        ulong hash = offset;
        for (int k = 0; k < Indices.Length; k++)
        {
            hash = (hash ^ Indices[k]) * prime;
            hash = (hash ^ (uint)BitConverter.SingleToInt32Bits(Values[k])) * prime;
        }
        return hash;
    }
}

/// <summary>
/// Stores a record's field vectors (one per vectorized column, in the dataset's column order) as bytes: for each
/// field a little-endian ushort count, then count × (ushort index, float value). The database keeps the bytes in
/// <c>DatasetRecords.FieldVectorData</c>; a vectors workbook keeps them in base64 in its Field Vectors column.
/// </summary>
public static class FieldVectorCodec
{
    public static byte[] Serialize(IReadOnlyList<SparseVector> fields)
    {
        int size = fields.Sum(f => 2 + 6 * f.Indices.Length);
        var bytes = new byte[size];
        var span = bytes.AsSpan();
        int at = 0;
        foreach (var field in fields)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(span[at..], (ushort)field.Indices.Length);
            at += 2;
            for (int k = 0; k < field.Indices.Length; k++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(span[at..], field.Indices[k]);
                BinaryPrimitives.WriteSingleLittleEndian(span[(at + 2)..], field.Values[k]);
                at += 6;
            }
        }
        return bytes;
    }

    /// <summary>The fields back; null for no data (a record from before field vectors). Throws on damaged data.</summary>
    public static SparseVector[]? Deserialize(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return null;
        var span = bytes.AsSpan();
        var fields = new List<SparseVector>();
        int at = 0;
        while (at < span.Length)
        {
            if (at + 2 > span.Length)
                throw new FormatException("The field vectors are cut short.");
            int count = BinaryPrimitives.ReadUInt16LittleEndian(span[at..]);
            at += 2;
            if (at + 6 * count > span.Length)
                throw new FormatException("The field vectors are cut short.");
            if (count == 0)
            {
                fields.Add(SparseVector.Empty);
                continue;
            }
            var indices = new ushort[count];
            var values = new float[count];
            for (int k = 0; k < count; k++)
            {
                indices[k] = BinaryPrimitives.ReadUInt16LittleEndian(span[at..]);
                values[k] = BinaryPrimitives.ReadSingleLittleEndian(span[(at + 2)..]);
                at += 6;
            }
            fields.Add(new SparseVector(indices, values));
        }
        return fields.ToArray();
    }

    public static string ToBase64(IReadOnlyList<SparseVector> fields) => Convert.ToBase64String(Serialize(fields));

    public static SparseVector[]? FromBase64(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : Deserialize(Convert.FromBase64String(text.Trim()));
}
