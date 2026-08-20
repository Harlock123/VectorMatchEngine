using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VectorMatchEngine.Core.Services;

/// <summary>Reads .xlsx workbooks with ClosedXML. Always operates on the first worksheet.</summary>
public class ExcelService
{
    private readonly ILogger<ExcelService> _logger;

    public ExcelService(ILogger<ExcelService>? logger = null)
        => _logger = logger ?? NullLogger<ExcelService>.Instance;

    /// <summary>Returns the header row column names from the first worksheet.</summary>
    public Task<List<string>> GetColumnNamesAsync(string filePath) => Task.Run(() =>
    {
        using var workbook = OpenWorkbook(filePath);
        var worksheet = FirstWorksheet(workbook);
        return ReadHeader(worksheet).Select(h => h.Name).ToList();
    });

    /// <summary>
    /// Returns all data rows (starting at the row after the header row).
    /// Each row is Dictionary&lt;columnName, cellValue&gt; with trimmed string values;
    /// null/empty cells become "".
    /// </summary>
    public Task<List<Dictionary<string, string>>> ReadRowsAsync(string filePath) => Task.Run(() =>
    {
        using var workbook = OpenWorkbook(filePath);
        var worksheet = FirstWorksheet(workbook);
        var header = ReadHeader(worksheet);
        var rows = new List<Dictionary<string, string>>();
        if (header.Count == 0)
            return rows;

        var headerRow = worksheet.FirstRowUsed();
        var lastRow = worksheet.LastRowUsed();
        if (headerRow is null || lastRow is null)
            return rows;

        for (int r = headerRow.RowNumber() + 1; r <= lastRow.RowNumber(); r++)
        {
            var xlRow = worksheet.Row(r);
            var values = new Dictionary<string, string>(header.Count, StringComparer.Ordinal);
            bool anyContent = false;

            foreach (var (column, name) in header)
            {
                var text = CellText(xlRow.Cell(column));
                if (text.Length > 0) anyContent = true;
                values[name] = text;
            }

            // Skip rows that are entirely blank rather than ingesting empty records.
            if (anyContent)
                rows.Add(values);
        }

        return rows;
    });

    private XLWorkbook OpenWorkbook(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("No file path was supplied.", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Excel file not found: {filePath}", filePath);

        try
        {
            return new XLWorkbook(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open workbook {FilePath}", filePath);
            throw new InvalidOperationException(
                $"'{Path.GetFileName(filePath)}' could not be opened as an .xlsx workbook. {ex.Message}", ex);
        }
    }

    private static IXLWorksheet FirstWorksheet(XLWorkbook workbook)
        => workbook.Worksheets.FirstOrDefault()
           ?? throw new InvalidOperationException("The workbook does not contain any worksheets.");

    /// <summary>
    /// Maps the header row to (column number, unique name) pairs. Blank header cells are skipped,
    /// and duplicate names are suffixed so they can be used as dictionary keys safely.
    /// </summary>
    private static List<(int Column, string Name)> ReadHeader(IXLWorksheet worksheet)
    {
        var header = new List<(int Column, string Name)>();
        var headerRow = worksheet.FirstRowUsed();
        if (headerRow is null)
            return header;

        var firstCell = headerRow.FirstCellUsed();
        var lastCell = headerRow.LastCellUsed();
        if (firstCell is null || lastCell is null)
            return header;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int c = firstCell.Address.ColumnNumber; c <= lastCell.Address.ColumnNumber; c++)
        {
            var name = CellText(headerRow.Cell(c));
            if (name.Length == 0)
                continue;   // skip blank header cells

            if (!seen.Add(name))
            {
                int suffix = 2;
                string candidate;
                do { candidate = $"{name} ({suffix++})"; } while (!seen.Add(candidate));
                name = candidate;
            }

            header.Add((c, name));
        }

        return header;
    }

    private static string CellText(IXLCell cell)
    {
        if (cell is null || cell.IsEmpty())
            return string.Empty;

        try
        {
            return cell.GetFormattedString().Trim();
        }
        catch
        {
            // Error cells (#REF!, #VALUE! ...) and exotic formats fall back to the raw value.
            try { return cell.Value.ToString()?.Trim() ?? string.Empty; }
            catch { return string.Empty; }
        }
    }
}
