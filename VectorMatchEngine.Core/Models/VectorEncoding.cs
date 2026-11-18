using VectorMatchEngine.Core.Services;
using KindNames = VectorMatchEngine.Core.Models.FieldKinds;

namespace VectorMatchEngine.Core.Models;

/// <summary>
/// How a dataset's vectors were made. Two datasets can only be matched if they were made the same way: the same
/// method, the same matching key (compared by fingerprint), the same number of sounds-alike columns, and the same
/// clean-up rules (<see cref="FieldCleaner"/>) applied to the same kinds of field.
/// </summary>
public sealed record VectorEncoding(
    string Method,
    string? KeyFingerprint,
    List<string> SoundsAlikeColumns,
    string? Cleanup = null,
    IReadOnlyDictionary<string, FieldKind>? FieldKinds = null)
{
    /// <summary>The original encoding: ML.NET's character trigrams, hashed without a key. Every older dataset has it.</summary>
    public const string Plain = "trigram";

    /// <summary>Character trigrams hashed with HMAC-SHA256 (keyed when there's a matching key), plus sounds-alike codes.</summary>
    public const string Keyed = "keyed-trigram";

    public static VectorEncoding Original { get; } = new(Plain, null, new List<string>());

    public bool HasKey => !string.IsNullOrEmpty(KeyFingerprint);

    /// <summary>The columns that were cleaned up, and how (Text columns aren't listed).</summary>
    public IReadOnlyDictionary<string, FieldKind> Kinds => FieldKinds ?? EmptyKinds;

    private static readonly IReadOnlyDictionary<string, FieldKind> EmptyKinds = new Dictionary<string, FieldKind>();

    /// <summary>A column's kind: <see cref="FieldKind.Text"/> unless it was cleaned up.</summary>
    public FieldKind KindOf(string column) => Kinds.TryGetValue(column, out var kind) ? kind : FieldKind.Text;

    /// <summary>A short description, such as "Keyed (3F9A-12C4-9B0E), sounds-alike: Last Name, cleaned: DOB (Date)".</summary>
    public string Display =>
        (Method == Plain ? "Plain" : HasKey ? $"Keyed ({KeyFingerprint})" : "No key")
        + (SoundsAlikeColumns.Count > 0 ? $", sounds-alike: {string.Join(", ", SoundsAlikeColumns)}" : "")
        + (Kinds.Count > 0
            ? $", cleaned: {string.Join(", ", Kinds.Select(k => $"{k.Key} ({KindNames.Label(k.Value)})"))}"
            : "");

    /// <summary>Why vectors made this way can't be compared with <paramref name="other"/>'s; null if they can.</summary>
    public string? IncompatibilityWith(VectorEncoding other)
    {
        if (Method != other.Method)
            return Method == Plain || other.Method == Plain
                ? "one was vectorized the original way and the other with a matching key, sounds-alike columns or field types"
                : $"they were vectorized differently ({Method} and {other.Method})";
        if (!string.Equals(KeyFingerprint ?? "", other.KeyFingerprint ?? "", StringComparison.OrdinalIgnoreCase))
            return HasKey && other.HasKey
                ? $"they were vectorized with different matching keys ({KeyFingerprint} and {other.KeyFingerprint})"
                : "one was vectorized with a matching key and the other without";
        if (SoundsAlikeColumns.Count != other.SoundsAlikeColumns.Count)
            return $"one encodes {SoundsAlikeColumns.Count} column(s) by sound and the other {other.SoundsAlikeColumns.Count}";
        if ((Cleanup ?? "") != (other.Cleanup ?? ""))
            return string.IsNullOrEmpty(Cleanup) || string.IsNullOrEmpty(other.Cleanup)
                ? "one had field types set (its values cleaned up) and the other didn't"
                : $"their values were cleaned up by different versions of the rules ({Cleanup} and {other.Cleanup})";
        var mine = KindList();
        var theirs = other.KindList();
        if (mine != theirs)
            return $"their field types differ ({(mine.Length > 0 ? mine : "none")} and {(theirs.Length > 0 ? theirs : "none")})";
        return null;
    }

    /// <summary>The kinds used, sorted, ignoring column names (the two datasets' columns may be named differently).</summary>
    private string KindList() => string.Join(", ", Kinds.Values.OrderBy(k => k).Select(KindNames.Label));
}

/// <summary>
/// What to vectorize with: an optional matching key, the columns to encode by how they sound, and what each column
/// holds (so its values can be cleaned up first).
/// </summary>
public sealed record VectorizeOptions(
    VectorKey? Key,
    IReadOnlyCollection<string> SoundsAlikeColumns,
    IReadOnlyDictionary<string, FieldKind>? FieldKinds = null)
{
    public static VectorizeOptions None { get; } = new(null, Array.Empty<string>());

    /// <summary>The columns with a kind other than <see cref="FieldKind.Text"/>.</summary>
    public IReadOnlyDictionary<string, FieldKind> CleanedKinds { get; } =
        (FieldKinds ?? new Dictionary<string, FieldKind>())
        .Where(k => k.Value != FieldKind.Text)
        .ToDictionary(k => k.Key, k => k.Value);

    /// <summary>The original ML.NET encoding applies when there's no key, no sounds-alike column and no field type.</summary>
    public bool UsesOriginal => Key is null && SoundsAlikeColumns.Count == 0 && CleanedKinds.Count == 0;

    public FieldKind KindOf(string column) => CleanedKinds.TryGetValue(column, out var kind) ? kind : FieldKind.Text;

    public VectorEncoding Encoding(IEnumerable<string> vectorizedColumns)
    {
        if (UsesOriginal)
            return VectorEncoding.Original;
        var columns = vectorizedColumns.ToList();
        var kinds = columns.Where(CleanedKinds.ContainsKey).ToDictionary(c => c, c => CleanedKinds[c]);
        return new VectorEncoding(VectorEncoding.Keyed, Key?.Fingerprint,
            columns.Where(c => SoundsAlikeColumns.Contains(c)).ToList(),
            kinds.Count > 0 ? FieldCleaner.Version : null,
            kinds);
    }
}
