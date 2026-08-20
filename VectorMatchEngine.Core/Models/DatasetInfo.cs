namespace VectorMatchEngine.Core.Models;

/// <summary>UI-facing summary of an ingested dataset.</summary>
public class DatasetInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public List<string> VectorizedColumns { get; set; } = new();
    public List<string> PreservedColumns { get; set; } = new();
    public int VectorDimensions { get; set; }
    public DateTime CreatedAt { get; set; }

    public string VectorizedColumnsDisplay => string.Join(", ", VectorizedColumns);
    public string PreservedColumnsDisplay => string.Join(", ", PreservedColumns);

    public override string ToString() => Name;
}
