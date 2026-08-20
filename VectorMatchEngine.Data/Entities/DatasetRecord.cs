using System.ComponentModel.DataAnnotations.Schema;

namespace VectorMatchEngine.Data.Entities;

[Table("DatasetRecords")]
public class DatasetRecord
{
    public long Id { get; set; }
    public int DatasetId { get; set; }
    public Dataset Dataset { get; set; } = null!;
    public int RowIndex { get; set; }
    public byte[] VectorData { get; set; } = Array.Empty<byte>(); // VARBINARY(MAX) - serialized float[]
    public string PreservedDataJson { get; set; } = "{}";          // NVARCHAR(MAX) - JSON object
}
