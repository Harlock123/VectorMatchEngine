using System.Globalization;
using System.IO.Compression;
using System.Xml;
using VectorMatchEngine.Core.Models;

namespace VectorMatchEngine.Core.Services;

/// <summary>
/// Reads a vectors workbook (what <see cref="VectorSheetExportService"/> writes) back, so its vectors can be loaded
/// into the database without vectorizing again. It is recognised by its Vectors sheet, whose header has a
/// "Vectorized Text" column followed by V0, V1 and so on.
///
/// Like the writer, it streams: the sheet is read straight from the zip with an <see cref="XmlReader"/>, since
/// ClosedXML would hold every one of its 500-plus cells a row in memory. It reads what Excel or another program
/// writes when the workbook is saved again too (shared strings, cell references, sheets in any order).
/// </summary>
public class VectorSheetImportService
{
    private const string TextHeader = "Vectorized Text";

    /// <summary>
    /// What the workbook holds, or null if it isn't a vectors workbook (or can't be opened as one: the ordinary
    /// import then reports why). Throws if it is one but its vectors can't be used with this version.
    /// </summary>
    public Task<VectorWorkbookInfo?> InspectAsync(string filePath) => Task.Run(() =>
    {
        ZipArchive zip;
        try { zip = ZipFile.OpenRead(filePath); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        using (zip)
        {
            Package package;
            try { package = Package.Open(zip); }
            catch (Exception ex) when (ex is XmlException or InvalidDataException or KeyNotFoundException)
            {
                return null;
            }
            return package.VectorsEntry is null ? null : Inspect(package, out _);
        }
    });

    /// <summary>Reads every row of a vectors workbook. Vectors are rescaled to length 1 if they were edited.</summary>
    public Task<VectorWorkbook> ReadAsync(
        string filePath, IProgress<(int current, int total, string message)>? progress = null) => Task.Run(() =>
    {
        using var zip = ZipFile.OpenRead(filePath);
        var package = Package.Open(zip);
        if (package.VectorsEntry is null)
            throw new InvalidOperationException($"'{Path.GetFileName(filePath)}' has no {VectorSheetExportService.VectorsSheetName} sheet.");
        var info = Inspect(package, out var layout)
                   ?? throw new InvalidOperationException(
                       $"The {VectorSheetExportService.VectorsSheetName} sheet's header isn't a vectors header ('{TextHeader}', then V0, V1 …).");

        int total = info.RowCount ?? 0;
        var preserved = new List<Dictionary<string, string>>(total);
        var vectors = new List<float[]>(total);
        foreach (var (rowNumber, cells) in package.ReadRows(package.VectorsEntry).Skip(1))
        {
            if (cells.All(string.IsNullOrWhiteSpace))
                continue;

            var values = new Dictionary<string, string>(layout.PreservedColumns.Count, StringComparer.Ordinal);
            foreach (var (name, column) in layout.PreservedColumns)
                values[name] = Cell(cells, column)?.Trim() ?? string.Empty;

            var vector = new float[info.Dimensions];
            double sumSquares = 0;
            for (int d = 0; d < vector.Length; d++)
            {
                var text = Cell(cells, layout.FirstVectorColumn + d);
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    || double.IsNaN(value) || double.IsInfinity(value))
                    throw new InvalidOperationException(
                        $"Cell {VectorSheetExportService.ColumnName(layout.FirstVectorColumn + d + 1)}{rowNumber} of the " +
                        $"{VectorSheetExportService.VectorsSheetName} sheet (V{d}) isn't a number: '{text}'.");
                vector[d] = (float)value;
                sumSquares += value * value;
            }

            // Matching takes the dot product as the cosine, which needs length 1; edited vectors may not have it.
            var length = Math.Sqrt(sumSquares);
            if (length > 0 && Math.Abs(length - 1) > 1e-4)
                for (int d = 0; d < vector.Length; d++)
                    vector[d] = (float)(vector[d] / length);

            preserved.Add(values);
            vectors.Add(vector);
            if (vectors.Count % 500 == 1)
                progress?.Report((vectors.Count, Math.Max(total, vectors.Count),
                    $"Reading row {vectors.Count:N0}{(total > 0 ? $" of {total:N0}" : "")}..."));
        }

        if (vectors.Count == 0)
            throw new InvalidOperationException($"The {VectorSheetExportService.VectorsSheetName} sheet has a header row but no data rows.");
        return new VectorWorkbook(info with { RowCount = vectors.Count }, preserved, vectors);
    });

    private sealed record Layout(List<(string Name, int Column)> PreservedColumns, int FirstVectorColumn);

    /// <summary>Recognises the Vectors sheet's header and reads the About sheet; null if it isn't a vectors header.</summary>
    private static VectorWorkbookInfo? Inspect(Package package, out Layout layout)
    {
        layout = new Layout(new(), 0);
        var header = package.ReadRows(package.VectorsEntry!).FirstOrDefault().Cells;
        if (header is null)
            return null;

        int textColumn = header.FindIndex(h => string.Equals(h?.Trim(), TextHeader, StringComparison.OrdinalIgnoreCase));
        if (textColumn < 0)
            return null;
        int dimensions = 0;
        while (string.Equals(Cell(header, textColumn + 1 + dimensions)?.Trim(), $"V{dimensions}", StringComparison.OrdinalIgnoreCase))
            dimensions++;
        if (dimensions == 0)
            return null;

        var about = package.AboutEntry is null
            ? new Dictionary<string, string>()
            : package.ReadRows(package.AboutEntry)
                .Where(r => !string.IsNullOrWhiteSpace(Cell(r.Cells, 0)))
                .GroupBy(r => Cell(r.Cells, 0)!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => Cell(g.First().Cells, 1)?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        if (dimensions != VectorizationService.VectorDimensions)
            throw new InvalidOperationException(
                $"This workbook's vectors have {dimensions} dimensions, but this version makes {VectorizationService.VectorDimensions}, " +
                "so they couldn't be matched with its datasets. Import the source workbook again instead.");
        if (about.TryGetValue("Method", out var method) && method.Length > 0
            && !string.Equals(method, VectorSheetExportService.MethodDescription, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"This workbook's vectors were made another way ({method}); this version uses " +
                $"{VectorSheetExportService.MethodDescription}. Import the source workbook again instead.");

        // The columns before the text are the preserved ones, after a leading Row column.
        int first = string.Equals(Cell(header, 0)?.Trim(), "Row", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var preserved = new List<(string Name, int Column)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int c = first; c < textColumn; c++)
        {
            var name = Cell(header, c)?.Trim();
            if (string.IsNullOrEmpty(name))
                continue;
            if (!seen.Add(name))
            {
                int suffix = 2;
                string candidate;
                do { candidate = $"{name} ({suffix++})"; } while (!seen.Add(candidate));
                name = candidate;
            }
            preserved.Add((name, c));
        }
        layout = new Layout(preserved, textColumn + 1);

        string About(string key) => about.TryGetValue(key, out var v) ? v : string.Empty;
        var vectorized = About("Vectorized columns")
            .Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        int? rows = int.TryParse(About("Rows"), NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var n) ? n : null;
        return new VectorWorkbookInfo(About("Dataset"), About("Source file"), vectorized,
            preserved.Select(p => p.Name).ToList(), dimensions, rows);
    }

    private static string? Cell(List<string?> cells, int column) => column >= 0 && column < cells.Count ? cells[column] : null;

    // ── The .xlsx package ─────────────────────────────────────────────────

    /// <summary>The parts of an .xlsx that matter here: the two sheets and the shared strings.</summary>
    private sealed class Package
    {
        private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string RelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const string PackageRelationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        private readonly List<string> _sharedStrings = new();

        public ZipArchiveEntry? VectorsEntry { get; private set; }
        public ZipArchiveEntry? AboutEntry { get; private set; }

        public static Package Open(ZipArchive zip)
        {
            var package = new Package();
            var workbookEntry = zip.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException("No xl/workbook.xml.");

            var targets = new Dictionary<string, string>(StringComparer.Ordinal);
            string? sharedStrings = null;
            if (zip.GetEntry("xl/_rels/workbook.xml.rels") is { } relsEntry)
            {
                using var reader = XmlReader.Create(relsEntry.Open());
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Relationship" || reader.NamespaceURI != PackageRelationshipsNs)
                        continue;
                    var id = reader.GetAttribute("Id");
                    var target = reader.GetAttribute("Target");
                    if (id is null || target is null)
                        continue;
                    var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                    targets[id] = path;
                    if (reader.GetAttribute("Type")?.EndsWith("/sharedStrings", StringComparison.Ordinal) == true)
                        sharedStrings = path;
                }
            }

            using (var reader = XmlReader.Create(workbookEntry.Open()))
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "sheet" || reader.NamespaceURI != Main)
                        continue;
                    var name = reader.GetAttribute("name");
                    var id = reader.GetAttribute("id", RelationshipsNs);
                    if (id is null || !targets.TryGetValue(id, out var path) || zip.GetEntry(path) is not { } entry)
                        continue;
                    if (string.Equals(name, VectorSheetExportService.VectorsSheetName, StringComparison.OrdinalIgnoreCase))
                        package.VectorsEntry ??= entry;
                    else if (string.Equals(name, VectorSheetExportService.AboutSheetName, StringComparison.OrdinalIgnoreCase))
                        package.AboutEntry ??= entry;
                }
            }

            if (package.VectorsEntry != null
                && (sharedStrings != null ? zip.GetEntry(sharedStrings) : zip.GetEntry("xl/sharedStrings.xml")) is { } stringsEntry)
                package.ReadSharedStrings(stringsEntry);
            return package;
        }

        private void ReadSharedStrings(ZipArchiveEntry entry)
        {
            using var reader = XmlReader.Create(entry.Open());
            while (reader.Read())
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "si" && reader.NamespaceURI == Main)
                    _sharedStrings.Add(ReadText(reader));
        }

        /// <summary>A sheet's rows in order, as (row number, cell values by 0-based column).</summary>
        public IEnumerable<(int Row, List<string?> Cells)> ReadRows(ZipArchiveEntry sheet)
        {
            using var reader = XmlReader.Create(sheet.Open());
            int lastRow = 0;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row" || reader.NamespaceURI != Main)
                    continue;
                int rowNumber = int.TryParse(reader.GetAttribute("r"), NumberStyles.None, CultureInfo.InvariantCulture, out var r) ? r : lastRow + 1;
                lastRow = rowNumber;
                var cells = new List<string?>();
                if (!reader.IsEmptyElement)
                {
                    int depth = reader.Depth;
                    int column = -1;
                    while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
                    {
                        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "c" || reader.NamespaceURI != Main)
                            continue;
                        column = ColumnOf(reader.GetAttribute("r")) ?? column + 1;
                        var value = ReadCell(reader);
                        while (cells.Count <= column)
                            cells.Add(null);
                        cells[column] = value;
                    }
                }
                yield return (rowNumber, cells);
            }
        }

        /// <summary>A cell's value as text, the reader on its &lt;c&gt; element.</summary>
        private string? ReadCell(XmlReader reader)
        {
            var type = reader.GetAttribute("t");
            if (reader.IsEmptyElement)
                return null;
            string? raw = null, inline = null;
            int depth = reader.Depth;
            while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
            {
                if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != Main)
                    continue;
                if (reader.LocalName == "v")
                    raw = reader.ReadElementContentAsString();
                else if (reader.LocalName == "is")
                    inline = ReadText(reader);
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                    break;
            }
            return type switch
            {
                "inlineStr" => inline ?? raw,
                "s" => int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < _sharedStrings.Count ? _sharedStrings[i] : null,
                "b" => raw == "1" ? "TRUE" : raw == "0" ? "FALSE" : raw,
                _ => raw ?? inline,
            };
        }

        /// <summary>The text of an &lt;si&gt; or &lt;is&gt; element (its &lt;t&gt; runs, without phonetic hints).</summary>
        private static string ReadText(XmlReader reader)
        {
            if (reader.IsEmptyElement)
                return string.Empty;
            var text = new System.Text.StringBuilder();
            int depth = reader.Depth;
            while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "rPh")
                    reader.Skip();
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t" && reader.NamespaceURI == Main)
                    text.Append(reader.ReadElementContentAsString());
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                    break;
            }
            return text.ToString();
        }

        /// <summary>The 0-based column of a reference like "AB12"; null if there isn't one.</summary>
        private static int? ColumnOf(string? reference)
        {
            if (string.IsNullOrEmpty(reference))
                return null;
            int column = 0, i = 0;
            for (; i < reference.Length && char.IsAsciiLetter(reference[i]); i++)
                column = column * 26 + (char.ToUpperInvariant(reference[i]) - 'A' + 1);
            return i == 0 ? null : column - 1;
        }
    }
}
