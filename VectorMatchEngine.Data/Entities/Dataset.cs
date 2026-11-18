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

    // How the vectors were made (see VectorEncoding); datasets can only be matched if these agree.
    public string Encoding { get; set; } = "trigram";             // NVARCHAR(32): "trigram" (original) or "keyed-trigram"
    public string? KeyFingerprint { get; set; }                   // NVARCHAR(32): the matching key's fingerprint, never the key
    public string SoundsAlikeColumnsJson { get; set; } = "[]";    // JSON array: columns encoded by how they sound
    public string? Cleanup { get; set; }                          // NVARCHAR(32): FieldCleaner.Version if any field type was set
    public string FieldKindsJson { get; set; } = "{}";            // JSON object: column -> FieldKind name (Text columns left out)

    /// <summary>Its records have FieldVectorData (datasets imported before field vectors don't).</summary>
    public bool HasFieldVectors { get; set; }
    public ICollection<DatasetRecord> Records { get; set; } = new List<DatasetRecord>();
}
