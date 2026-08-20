namespace VectorMatchEngine.Core.Models;

public class MatchedPair
{
    public long RecordAId { get; set; }
    public long RecordBId { get; set; }
    public double SimilarityScore { get; set; }
    public Dictionary<string, string> DatasetAPreserved { get; set; } = new();
    public Dictionary<string, string> DatasetBPreserved { get; set; } = new();
}
