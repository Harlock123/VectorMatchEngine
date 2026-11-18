namespace VectorMatchEngine.Core.Models;

public class MatchedPair
{
    public long RecordAId { get; set; }
    public long RecordBId { get; set; }
    public double SimilarityScore { get; set; }

    /// <summary>
    /// Each paired field's score (the job's <see cref="MatchSettings.Fields"/>, in order), null for a field blank on
    /// either side. Null altogether when the job had no paired fields or a dataset has no field vectors.
    /// </summary>
    public double?[]? FieldScores { get; set; }

    /// <summary>
    /// The best score either record had with any other record it was compared with (above the threshold or not);
    /// null if neither had another candidate.
    /// </summary>
    public double? RunnerUpScore { get; set; }

    /// <summary>How far ahead of the runner-up this pair is. Small or negative means an ambiguous match.</summary>
    public double? Gap => RunnerUpScore is { } runnerUp ? SimilarityScore - runnerUp : null;

    public Dictionary<string, string> DatasetAPreserved { get; set; } = new();
    public Dictionary<string, string> DatasetBPreserved { get; set; } = new();
}
