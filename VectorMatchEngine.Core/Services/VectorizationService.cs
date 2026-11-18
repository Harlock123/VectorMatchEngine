using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms;
using Microsoft.ML.Transforms.Text;
using VectorMatchEngine.Core.Models;

namespace VectorMatchEngine.Core.Services;

/// <summary>
/// Turns a row's selected text columns into an L2-normalized character-trigram vector.
///
/// The pipeline hashes trigrams into a fixed 2^<see cref="NumberOfBits"/> space rather than
/// learning a per-dataset vocabulary. That matters: a fitted vocabulary would assign different
/// meanings to the same vector index in different datasets, so vectors from Dataset A and
/// Dataset B could not be compared. Hashing gives every dataset the same axes, which is what
/// makes cross-dataset cosine similarity meaningful.
///
/// That fixed space is public, though: anyone with ML.NET can vectorize guesses (names, dates of birth) and find the
/// ones that match a vector. So with a matching key (<see cref="VectorKey"/>), or with sounds-alike columns, rows
/// are vectorized another way (<see cref="VectorEncoding.Keyed"/>): each trigram is hashed with HMAC-SHA256 under
/// the key, which nobody without it can reproduce, and sounds-alike columns contribute their words' Metaphone codes
/// instead of their spelling. Without either, the original ML.NET pipeline runs, so older datasets still match.
/// </summary>
public class VectorizationService
{
    /// <summary>Hash space size, in bits. 9 bits => 512 dimensions (2 KB per stored record).</summary>
    public const int NumberOfBits = 9;

    /// <summary>Dimensionality of every vector this service produces.</summary>
    public const int VectorDimensions = 1 << NumberOfBits;

    /// <summary>Character n-gram length.</summary>
    public const int NgramLength = 3;

    private readonly ILogger<VectorizationService> _logger;

    public VectorizationService(ILogger<VectorizationService>? logger = null)
        => _logger = logger ?? NullLogger<VectorizationService>.Instance;

    /// <summary>
    /// Vectorizes every row. The returned list is in the same order as <paramref name="rows"/>
    /// and always has the same length.
    /// </summary>
    public List<float[]> VectorizeRows(
        List<Dictionary<string, string>> rows,
        List<string> vectorizedColumns,
        VectorizeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(vectorizedColumns);
        options ??= VectorizeOptions.None;
        if (!options.UsesOriginal)
        {
            if (vectorizedColumns.Count == 0)
                throw new ArgumentException(
                    "At least one column must be selected for vectorization.", nameof(vectorizedColumns));
            return VectorizeKeyed(rows, vectorizedColumns, options);
        }

        var result = new List<float[]>(rows.Count);
        if (rows.Count == 0)
            return result;

        if (vectorizedColumns.Count == 0)
            throw new ArgumentException(
                "At least one column must be selected for vectorization.", nameof(vectorizedColumns));

        var textRows = rows
            .Select(row => new TextRow { CombinedText = BuildCombinedText(row, vectorizedColumns) })
            .ToList();

        try
        {
            var mlContext = new MLContext(seed: 0);
            var dataView = mlContext.Data.LoadFromEnumerable(textRows);

            var pipeline =
                mlContext.Transforms.Text.NormalizeText(
                        outputColumnName: "NormalizedText",
                        inputColumnName: nameof(TextRow.CombinedText),
                        caseMode: TextNormalizingEstimator.CaseMode.Lower,
                        keepDiacritics: false,
                        keepPunctuations: false,
                        keepNumbers: true)
                    .Append(mlContext.Transforms.Text.TokenizeIntoCharactersAsKeys(
                        outputColumnName: "Chars",
                        inputColumnName: "NormalizedText",
                        useMarkerCharacters: true))
                    .Append(mlContext.Transforms.Text.ProduceHashedNgrams(
                        outputColumnName: "HashedNgrams",
                        inputColumnName: "Chars",
                        numberOfBits: NumberOfBits,
                        ngramLength: NgramLength,
                        skipLength: 0,
                        useAllLengths: false))
                    .Append(mlContext.Transforms.NormalizeLpNorm(
                        outputColumnName: "Features",
                        inputColumnName: "HashedNgrams",
                        norm: LpNormNormalizingEstimatorBase.NormFunction.L2));

            var model = pipeline.Fit(dataView);
            var transformed = model.Transform(dataView);

            foreach (var vectorRow in mlContext.Data.CreateEnumerable<VectorRow>(transformed, reuseRowObject: false))
                result.Add(Normalize(vectorRow.Features));
        }
        catch (Exception ex)
        {
            // Featurization is all-or-nothing inside a single ML.NET pass, so a failure here
            // falls back to zero vectors: the batch still ingests, those rows simply never match.
            _logger.LogError(ex, "ML.NET featurization failed for a batch of {RowCount} rows.", rows.Count);
            result.Clear();
        }

        // Guarantee a 1:1 alignment with the input rows regardless of what the pipeline returned.
        if (result.Count < rows.Count)
        {
            if (result.Count > 0)
                _logger.LogWarning(
                    "Featurization returned {Actual} vectors for {Expected} rows; padding with zero vectors.",
                    result.Count, rows.Count);

            while (result.Count < rows.Count)
                result.Add(new float[VectorDimensions]);
        }
        else if (result.Count > rows.Count)
        {
            result.RemoveRange(rows.Count, result.Count - rows.Count);
        }

        return result;
    }

    // ── The keyed encoding ────────────────────────────────────────────────

    /// <summary>
    /// <see cref="VectorEncoding.Keyed"/>: each column's value cleaned up by its field type (<see cref="FieldCleaner"/>),
    /// then the spelled columns' text, normalized like the ML.NET pipeline (lower case,
    /// no accents or punctuation), as marker-wrapped character trigrams; each sounds-alike column's words as the
    /// trigrams of their Metaphone codes; every trigram hashed with HMAC-SHA256 under the key (an empty key without
    /// one) into the 2^<see cref="NumberOfBits"/> buckets; then counted and L2-normalized.
    /// </summary>
    private static List<float[]> VectorizeKeyed(
        List<Dictionary<string, string>> rows, List<string> vectorizedColumns, VectorizeOptions options)
    {
        var hasher = new KeyedHasher(options.Key?.Bytes ?? Array.Empty<byte>());
        var spelled = vectorizedColumns.Where(c => !options.SoundsAlikeColumns.Contains(c)).ToList();
        var sounded = vectorizedColumns.Where(c => options.SoundsAlikeColumns.Contains(c)).ToList();

        var result = new List<float[]>(rows.Count);
        foreach (var row in rows)
        {
            var vector = new float[VectorDimensions];
            var cleaned = options.CleanedKinds.Count > 0 ? CleanRow(row, vectorizedColumns, options) : row;
            foreach (var gram in Trigrams(NormalizeForKeyed(BuildCombinedText(cleaned, spelled))))
                vector[hasher.Bucket("t|" + gram)]++;
            foreach (var column in sounded)
                AddSoundCodes(vector, hasher, cleaned.GetValueOrDefault(column, string.Empty));
            NormalizeInPlace(vector);
            result.Add(vector);
        }
        return result;
    }

    /// <summary>
    /// One vector per vectorized column for every row, in <paramref name="vectorizedColumns"/>' order: the column's
    /// value alone, encoded as the keyed encoding encodes it (cleaned up by its field type, spelled or by sound,
    /// HMAC-SHA256 under the matching key, or an empty key without one), L2-normalized. Match jobs score these field
    /// by field, explain scores with them and block on them. They're made for every encoding, the original one too,
    /// so that any two datasets with the same key and settings have comparable fields.
    /// </summary>
    public List<SparseVector[]> VectorizeFields(
        List<Dictionary<string, string>> rows, List<string> vectorizedColumns, VectorizeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(vectorizedColumns);
        options ??= VectorizeOptions.None;
        var hasher = new KeyedHasher(options.Key?.Bytes ?? Array.Empty<byte>());
        var soundsAlike = vectorizedColumns.Select(c => options.SoundsAlikeColumns.Contains(c)).ToArray();

        var result = new List<SparseVector[]>(rows.Count);
        var vector = new float[VectorDimensions];
        foreach (var row in rows)
        {
            var fields = new SparseVector[vectorizedColumns.Count];
            for (int f = 0; f < fields.Length; f++)
            {
                var column = vectorizedColumns[f];
                var value = FieldCleaner.Clean(options.KindOf(column), row.GetValueOrDefault(column, string.Empty));
                Array.Clear(vector);
                if (soundsAlike[f])
                    AddSoundCodes(vector, hasher, value);
                else
                    foreach (var gram in Trigrams(NormalizeForKeyed(value)))
                        vector[hasher.Bucket("t|" + gram)]++;
                NormalizeInPlace(vector);
                fields[f] = SparseVector.FromDense(vector);
            }
            result.Add(fields);
        }
        return result;
    }

    /// <summary>A copy of the row's vectorized columns, each cleaned up by its field type.</summary>
    private static Dictionary<string, string> CleanRow(
        Dictionary<string, string> row, List<string> vectorizedColumns, VectorizeOptions options)
    {
        var cleaned = new Dictionary<string, string>(vectorizedColumns.Count, StringComparer.Ordinal);
        foreach (var column in vectorizedColumns)
            cleaned[column] = FieldCleaner.Clean(options.KindOf(column), row.GetValueOrDefault(column, string.Empty));
        return cleaned;
    }

    /// <summary>
    /// Adds a sounds-alike value's codes. A code is shorter than its word (JN for John), so its trigrams are weighted
    /// to carry about the share of the vector a word's own would (a word is typically its code's length plus two).
    /// The weight depends on the code alone, so names that sound alike are encoded identically.
    /// </summary>
    private static void AddSoundCodes(float[] vector, KeyedHasher hasher, string value)
    {
        foreach (var code in SoundCodes(value))
        {
            var grams = Trigrams(code).ToList();
            float weight = (float)Math.Sqrt((code.Length + 2.0) / grams.Count);
            foreach (var gram in grams)
                vector[hasher.Bucket("p|" + gram)] += weight;
        }
    }

    private static void NormalizeInPlace(float[] vector)
    {
        double length = Math.Sqrt(vector.Sum(x => (double)x * x));
        if (length > 0)
            for (int d = 0; d < vector.Length; d++)
                vector[d] = (float)(vector[d] / length);
    }

    /// <summary>A feature's bucket: the first four bytes of its HMAC-SHA256 under the key, mod the vector size.</summary>
    private sealed class KeyedHasher(byte[] key)
    {
        private readonly Dictionary<string, int> _buckets = new(StringComparer.Ordinal);

        public int Bucket(string feature)
        {
            if (!_buckets.TryGetValue(feature, out var bucket))
            {
                var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(feature));
                bucket = (int)(BinaryPrimitives.ReadUInt32LittleEndian(hash) % VectorDimensions);
                _buckets[feature] = bucket;
            }
            return bucket;
        }
    }

    /// <summary>A sounds-alike column's words as Metaphone codes (a word with no letters, such as a number, as itself).</summary>
    public static IEnumerable<string> SoundCodes(string value) =>
        NormalizeForKeyed(value).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => Metaphone.Encode(word) is { Length: > 0 } code ? code : word);

    /// <summary>Lower case, accents and punctuation dropped, whitespace collapsed (what ML.NET's NormalizeText does here).</summary>
    private static string NormalizeForKeyed(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool space = false;
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsWhiteSpace(ch))
            {
                space = sb.Length > 0;
                continue;
            }
            if (!char.IsLetterOrDigit(ch))
                continue;
            if (space) { sb.Append(' '); space = false; }
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>Character trigrams of a text wrapped in start and end markers (a blank text has none).</summary>
    private static IEnumerable<string> Trigrams(string text)
    {
        if (text.Length == 0)
            yield break;
        var marked = "\u0002" + text + "\u0003";
        for (int i = 0; i + NgramLength <= marked.Length; i++)
            yield return marked.Substring(i, NgramLength);
        if (marked.Length < NgramLength)
            yield return marked;
    }

    /// <summary>
    /// What a row's vector is made from, readable: the spelled columns' text, then each sounds-alike column's
    /// Metaphone codes in brackets. For the optional Vectorized Text column of a vectors workbook.
    /// </summary>
    public static string DescribeInput(Dictionary<string, string> row, List<string> vectorizedColumns, VectorizeOptions? options)
    {
        if (options is { CleanedKinds.Count: > 0 })
            row = CleanRow(row, vectorizedColumns, options);
        if (options is null || options.SoundsAlikeColumns.Count == 0)
            return BuildCombinedText(row, vectorizedColumns);
        var parts = vectorizedColumns.Select(c => options.SoundsAlikeColumns.Contains(c)
                ? string.Join(' ', SoundCodes(row.GetValueOrDefault(c, string.Empty)).Select(code => $"[{code}]"))
                : (row.GetValueOrDefault(c, string.Empty) ?? string.Empty).Trim().ToLowerInvariant())
            .Where(p => p.Length > 0);
        return string.Join(' ', parts);
    }

    /// <summary>
    /// Concatenates the selected columns (in the given order), separated by a single space,
    /// lowercased and trimmed.
    /// </summary>
    public static string BuildCombinedText(Dictionary<string, string> row, List<string> vectorizedColumns)
    {
        var parts = vectorizedColumns
            .Select(column => row.TryGetValue(column, out var value) ? value : string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim());

        return string.Join(' ', parts).ToLowerInvariant().Trim();
    }

    /// <summary>Serializes float[] to byte[] for VARBINARY(MAX) storage.</summary>
    public byte[] SerializeVector(float[] vector)
    {
        if (vector is null || vector.Length == 0)
            return Array.Empty<byte>();

        return MemoryMarshal.AsBytes<float>(vector.AsSpan()).ToArray();
    }

    /// <summary>Deserializes byte[] back into float[].</summary>
    public float[] DeserializeVector(byte[] bytes)
    {
        if (bytes is null || bytes.Length < sizeof(float))
            return Array.Empty<float>();

        return MemoryMarshal.Cast<byte, float>(bytes.AsSpan()).ToArray();
    }

    /// <summary>Guards against a null/short/NaN vector reaching the similarity loop.</summary>
    private static float[] Normalize(float[]? features)
    {
        if (features is null || features.Length == 0)
            return new float[VectorDimensions];

        for (int i = 0; i < features.Length; i++)
        {
            if (float.IsNaN(features[i]) || float.IsInfinity(features[i]))
                features[i] = 0f;
        }

        return features;
    }

    private sealed class TextRow
    {
        public string CombinedText { get; set; } = string.Empty;
    }

    private sealed class VectorRow
    {
        [VectorType(VectorDimensions)]
        public float[]? Features { get; set; }
    }
}
