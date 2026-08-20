using System.ComponentModel.DataAnnotations.Schema;

namespace VectorMatchEngine.Data.Entities;

[Table("Datasets")]
public class Dataset
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;          // NVARCHAR(256)
    public string FileName { get; set; } = string.Empty;      // NVARCHAR(512)
    public int RowCount { get; set; }
    public string VectorizedColumnsJson { get; set; } = "[]"; // JSON array of strings
    public string PreservedColumnsJson { get; set; } = "[]";  // JSON array of strings
    public int VectorDimensions { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<DatasetRecord> Records { get; set; } = new List<DatasetRecord>();
}
