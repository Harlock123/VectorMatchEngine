namespace VectorMatchEngine.Core.Models;

/// <summary>What a vectors workbook (written by Import, "Another spreadsheet") says about itself.</summary>
public sealed record VectorWorkbookInfo(
    string DatasetName,
    string SourceFile,
    List<string> VectorizedColumns,
    List<string> PreservedColumns,
    int Dimensions,
    int? RowCount,
    VectorEncoding Encoding)
{
    public string Summary =>
        $"{(RowCount is { } n ? $"{n:N0} rows" : "Rows")} of {Dimensions}-dimension vectors" +
        (SourceFile.Length > 0 ? $" made from {SourceFile}" : "") +
        (VectorizedColumns.Count > 0 ? $" (vectorized: {string.Join(", ", VectorizedColumns)})" : "") +
        $". Preserved columns: {(PreservedColumns.Count > 0 ? string.Join(", ", PreservedColumns) : "none")}." +
        $" Encoding: {Encoding.Display}.";
}

/// <summary>A vectors workbook's rows: each one's preserved values and its vector, in sheet order.</summary>
public sealed record VectorWorkbook(
    VectorWorkbookInfo Info,
    List<Dictionary<string, string>> Preserved,
    List<float[]> Vectors,
    List<SparseVector[]>? FieldVectors = null);
