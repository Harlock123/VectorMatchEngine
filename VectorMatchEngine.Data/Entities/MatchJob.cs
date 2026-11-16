using System.ComponentModel.DataAnnotations.Schema;

namespace VectorMatchEngine.Data.Entities;

[Table("MatchJobs")]
public class MatchJob
{
    public int Id { get; set; }
    public int DatasetAId { get; set; }
    public Dataset DatasetA { get; set; } = null!;
    public int DatasetBId { get; set; }
    public Dataset DatasetB { get; set; } = null!;
    public double Threshold { get; set; }
    public string Status { get; set; } = "Pending"; // Pending | Running | Completed | Failed
    public int TotalMatchesFound { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public ICollection<MatchResult> Results { get; set; } = new List<MatchResult>();

    // Display helpers for the MatchJobs DataGrid.
    [NotMapped] public string DatasetAName => DatasetA?.Name ?? $"#{DatasetAId}";
    /// <summary>Set by the run that made it: whether SQL Server did the comparing (VECTOR_DISTANCE).</summary>
    [NotMapped] public bool MatchedInDatabase { get; set; }

    [NotMapped] public string DatasetBName => DatasetB?.Name ?? $"#{DatasetBId}";
}
