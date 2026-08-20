using ClosedXML.Excel;
using VectorMatchEngine.Core.Models;

namespace VectorMatchEngine.Core.Services;

/// <summary>Writes match results to an .xlsx workbook.</summary>
public class ExcelExportService
{
    private const string SheetName = "Match Results";

    public Task ExportMatchResultsAsync(
        string outputPath,
        List<MatchedPair> pairs,
        List<string> columnsA,
        List<string> columnsB) => Task.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("No output path was supplied.", nameof(outputPath));

        ArgumentNullException.ThrowIfNull(pairs);
        columnsA ??= new List<string>();
        columnsB ??= new List<string>();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(SheetName);

        // Header row
        int column = 1;
        worksheet.Cell(1, column++).Value = "Similarity";
        foreach (var name in columnsA)
            worksheet.Cell(1, column++).Value = $"A_{name}";
        foreach (var name in columnsB)
            worksheet.Cell(1, column++).Value = $"B_{name}";

        int lastColumn = column - 1;
        worksheet.Range(1, 1, 1, lastColumn).Style.Font.Bold = true;

        // Data rows
        int row = 2;
        foreach (var pair in pairs)
        {
            column = 1;
            worksheet.Cell(row, column++).Value = pair.SimilarityScore.ToString("0.0000");

            foreach (var name in columnsA)
                worksheet.Cell(row, column++).Value = pair.DatasetAPreserved.GetValueOrDefault(name, string.Empty);

            foreach (var name in columnsB)
                worksheet.Cell(row, column++).Value = pair.DatasetBPreserved.GetValueOrDefault(name, string.Empty);

            row++;
        }

        worksheet.SheetView.FreezeRows(1);
        worksheet.Columns(1, lastColumn).AdjustToContents();

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        workbook.SaveAs(outputPath);
    });
}
