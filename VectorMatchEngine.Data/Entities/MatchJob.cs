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

    /// <summary>The job's MatchSettings as JSON (field pairing, scoring, one-to-one, blocking); null for older jobs.</summary>
    public string? SettingsJson { get; set; }                     // NVARCHAR(MAX)

    /// <summary>How many pairs were scored (fewer than A × B with blocking); null when SQL Server did the comparing.</summary>
    public long? PairsCompared { get; set; }
    public ICollection<MatchResult> Results { get; set; } = new List<MatchResult>();

    // Display helpers for the MatchJobs DataGrid.
    [NotMapped] public string DatasetAName => DatasetA?.Name ?? $"#{DatasetAId}";
    /// <summary>Set by the run that made it: whether SQL Server did the comparing (VECTOR_DISTANCE).</summary>
    [NotMapped] public bool MatchedInDatabase { get; set; }

    [NotMapped] public string DatasetBName => DatasetB?.Name ?? $"#{DatasetBId}";

    /// <summary>How the job compared records, for the jobs list.</summary>
    [NotMapped] public string SettingsDisplay => DataService.SettingsOf(this).Display;
}
