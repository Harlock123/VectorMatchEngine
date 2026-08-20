namespace VectorMatchEngine.Core.Models;

public class ColumnSelection
{
    public string Name { get; set; } = string.Empty;
    public bool IsVectorized { get; set; }  // will be embedded into float[] vector
    public bool IsPreserved { get; set; }   // will be stored as-is in JSON
    // A column CAN be both vectorized and preserved simultaneously
}
