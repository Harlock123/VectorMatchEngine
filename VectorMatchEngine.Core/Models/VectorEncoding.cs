namespace VectorMatchEngine.Core.Models;

/// <summary>
/// How a dataset's vectors were made. Two datasets can only be matched if they were made the same way: the same
/// method, the same matching key (compared by fingerprint), and the same number of sounds-alike columns.
/// </summary>
public sealed record VectorEncoding(string Method, string? KeyFingerprint, List<string> SoundsAlikeColumns)
{
    /// <summary>The original encoding: ML.NET's character trigrams, hashed without a key. Every older dataset has it.</summary>
    public const string Plain = "trigram";

    /// <summary>Character trigrams hashed with HMAC-SHA256 (keyed when there's a matching key), plus sounds-alike codes.</summary>
    public const string Keyed = "keyed-trigram";

    public static VectorEncoding Original { get; } = new(Plain, null, new List<string>());

    public bool HasKey => !string.IsNullOrEmpty(KeyFingerprint);

    /// <summary>A short description, such as "Keyed (3F9A-12C4-9B0E), sounds-alike: Last Name".</summary>
    public string Display =>
        (Method == Plain ? "Plain" : HasKey ? $"Keyed ({KeyFingerprint})" : "No key")
        + (SoundsAlikeColumns.Count > 0 ? $", sounds-alike: {string.Join(", ", SoundsAlikeColumns)}" : "");

    /// <summary>Why vectors made this way can't be compared with <paramref name="other"/>'s; null if they can.</summary>
    public string? IncompatibilityWith(VectorEncoding other)
    {
        if (Method != other.Method)
            return Method == Plain || other.Method == Plain
                ? "one was vectorized the original way and the other with a matching key or sounds-alike columns"
                : $"they were vectorized differently ({Method} and {other.Method})";
        if (!string.Equals(KeyFingerprint ?? "", other.KeyFingerprint ?? "", StringComparison.OrdinalIgnoreCase))
            return HasKey && other.HasKey
                ? $"they were vectorized with different matching keys ({KeyFingerprint} and {other.KeyFingerprint})"
                : "one was vectorized with a matching key and the other without";
        if (SoundsAlikeColumns.Count != other.SoundsAlikeColumns.Count)
            return $"one encodes {SoundsAlikeColumns.Count} column(s) by sound and the other {other.SoundsAlikeColumns.Count}";
        return null;
    }
}

/// <summary>What to vectorize with: an optional matching key, and the columns to encode by how they sound.</summary>
public sealed record VectorizeOptions(Services.VectorKey? Key, IReadOnlyCollection<string> SoundsAlikeColumns)
{
    public static VectorizeOptions None { get; } = new(null, Array.Empty<string>());

    /// <summary>The original ML.NET encoding applies when there's neither a key nor a sounds-alike column.</summary>
    public bool UsesOriginal => Key is null && SoundsAlikeColumns.Count == 0;

    public VectorEncoding Encoding(IEnumerable<string> vectorizedColumns) => UsesOriginal
        ? VectorEncoding.Original
        : new VectorEncoding(VectorEncoding.Keyed, Key?.Fingerprint,
            vectorizedColumns.Where(c => SoundsAlikeColumns.Contains(c)).ToList());
}
