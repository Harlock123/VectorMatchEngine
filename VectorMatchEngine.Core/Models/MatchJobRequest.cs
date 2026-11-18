namespace VectorMatchEngine.Core.Models;

public class MatchJobRequest
{
    public int DatasetAId { get; set; }
    public int DatasetBId { get; set; }
    public double SimilarityThreshold { get; set; } = 0.85;

    /// <summary>
    /// Compare inside SQL Server with its VECTOR type when the server has it (SQL Server 2025, Azure SQL);
    /// otherwise, or when false, the app compares the vectors itself. Settings that need the app's own matcher
    /// (<see cref="MatchSettings.NeedsAppMatching"/>) always run in the app.
    /// </summary>
    public bool UseDatabaseVectors { get; set; } = true;

    public MatchSettings Settings { get; set; } = new();
}
