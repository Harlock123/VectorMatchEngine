namespace VectorMatchEngine.Core.Models;

/// <summary>
/// How a match job compares records, beyond its threshold: field by field or whole rows, one match per record or
/// every pair, and which candidates to compare at all (blocking). Saved with the job as JSON (MatchJob.SettingsJson).
/// </summary>
public sealed class MatchSettings
{
    /// <summary>
    /// The fields paired between the datasets. With <see cref="ScoreByField"/> they make the score; either way, when
    /// both datasets have field vectors, each pair's field scores are worked out to explain it.
    /// </summary>
    public List<FieldPairing> Fields { get; set; } = new();

    /// <summary>Score as the weighted average of the paired fields' scores, instead of the whole-row vectors' cosine.</summary>
    public bool ScoreByField { get; set; }

    /// <summary>Keep each record's best match only (greedily, highest score first), so no record is in two pairs.</summary>
    public bool OneToOne { get; set; }

    /// <summary>Also compare pairs whose whole-row vectors land in the same random-hyperplane (LSH) bucket.</summary>
    public bool ApproximateBlocking { get; set; }

    public bool UsesFieldBlocking => Fields.Any(f => f.Block);
    public bool UsesBlocking => ApproximateBlocking || UsesFieldBlocking;

    /// <summary>
    /// Whether anything here needs the app's own matcher rather than SQL Server's VECTOR_DISTANCE. Fields that only
    /// explain a whole-row score don't: SQL Server can still do that job, just without the explanation columns.
    /// </summary>
    public bool NeedsAppMatching => ScoreByField || OneToOne || UsesBlocking || Fields.Any(f => f.MustMatch);

    /// <summary>A short description, such as "Field by field · one match per record · blocked on DOB, LSH".</summary>
    public string Display
    {
        get
        {
            var parts = new List<string> { ScoreByField ? "Field by field" : "Whole row" };
            if (OneToOne)
                parts.Add("one match per record");
            var blocks = Fields.Where(f => f.Block).Select(f => f.Label).ToList();
            if (ApproximateBlocking)
                blocks.Add("LSH");
            if (blocks.Count > 0)
                parts.Add("blocked on " + string.Join(", ", blocks));
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>One of Dataset A's vectorized columns paired with one of Dataset B's.</summary>
public sealed class FieldPairing
{
    /// <summary>The column's position among Dataset A's vectorized columns (its field vector's index).</summary>
    public int IndexA { get; set; }
    public int IndexB { get; set; }
    public string NameA { get; set; } = string.Empty;
    public string NameB { get; set; } = string.Empty;

    /// <summary>Its share of a field-by-field score.</summary>
    public double Weight { get; set; } = 1;

    /// <summary>A pair is only a match if this field's own score reaches the threshold (and neither side is blank).</summary>
    public bool MustMatch { get; set; }

    /// <summary>Compare records that have exactly the same value here (after clean-up). Several block fields: any of them.</summary>
    public bool Block { get; set; }

    public string Label => string.Equals(NameA, NameB, StringComparison.OrdinalIgnoreCase) ? NameA : $"{NameA} / {NameB}";
}

/// <summary>A record as the matcher sees it: its id, whole-row vector, field vectors (if it has them) and preserved values.</summary>
public sealed record MatchRecord(
    long Id,
    float[] Vector,
    SparseVector[]? Fields,
    Dictionary<string, string> Preserved);

/// <summary>What a run found, and how much comparing it took.</summary>
public sealed class MatchRun
{
    public List<MatchedPair> Pairs { get; init; } = new();

    /// <summary>The pairs actually scored: all of A × B without blocking, fewer with it.</summary>
    public long PairsCompared { get; init; }
}
