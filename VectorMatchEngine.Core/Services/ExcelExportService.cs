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
        List<string> columnsB,
        List<string>? fieldLabels = null) => Task.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("No output path was supplied.", nameof(outputPath));

        ArgumentNullException.ThrowIfNull(pairs);
        columnsA ??= new List<string>();
        columnsB ??= new List<string>();
        fieldLabels ??= new List<string>();
        // Explanation columns only when the pairs have them: runner-up and gap, then one score per paired field.
        bool withRunnerUp = pairs.Any(p => p.RunnerUpScore is not null);
        int fieldCount = pairs.Any(p => p.FieldScores is not null) ? fieldLabels.Count : 0;

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(SheetName);

        // Header row
        int column = 1;
        worksheet.Cell(1, column++).Value = "Similarity";
        if (withRunnerUp)
        {
            worksheet.Cell(1, column++).Value = "Runner-up";
            worksheet.Cell(1, column++).Value = "Gap";
        }
        for (int f = 0; f < fieldCount; f++)
            worksheet.Cell(1, column++).Value = $"Score: {fieldLabels[f]}";
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
            if (withRunnerUp)
            {
                SetScore(worksheet.Cell(row, column++), pair.RunnerUpScore);
                SetScore(worksheet.Cell(row, column++), pair.Gap);
            }
            for (int f = 0; f < fieldCount; f++)
                SetScore(worksheet.Cell(row, column++),
                    pair.FieldScores is { } scores && f < scores.Length ? scores[f] : null);

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

    /// <summary>A score as a number shown to four places; blank when there's none (no rival, or a blank field).</summary>
    private static void SetScore(IXLCell cell, double? score)
    {
        if (score is not { } value)
            return;
        cell.Value = Math.Round(value, 4);
        cell.Style.NumberFormat.Format = "0.0000";
    }
}
