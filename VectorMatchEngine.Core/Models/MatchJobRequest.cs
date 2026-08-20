namespace VectorMatchEngine.Core.Models;

public class MatchJobRequest
{
    public int DatasetAId { get; set; }
    public int DatasetBId { get; set; }
    public double SimilarityThreshold { get; set; } = 0.85;
}
