using System.ComponentModel.DataAnnotations.Schema;

namespace VectorMatchEngine.Data.Entities;

[Table("MatchResults")]
public class MatchResult
{
    public long Id { get; set; }
    public int MatchJobId { get; set; }
    public MatchJob MatchJob { get; set; } = null!;
    public long RecordAId { get; set; }
    public DatasetRecord RecordA { get; set; } = null!;
    public long RecordBId { get; set; }
    public DatasetRecord RecordB { get; set; } = null!;
    public double SimilarityScore { get; set; }
}
