using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using VectorMatchEngine.Core.Models;

namespace VectorMatchEngine.Core.Services;

/// <summary>
/// Writes a dataset's vectors to an .xlsx workbook instead of the database: a "Vectors" sheet with one row per
/// source row (its row number, the preserved columns, optionally the text that was vectorized, then one numeric
/// column per vector dimension, V0 to V511), and an "About" sheet recording how the vectors were made.
/// The vectorized text is plain readable data, so it's left out unless asked for.
///
/// Each row is over 500 cells, so the workbook is streamed straight into the file: the zip is written in create
/// mode and each sheet with an <see cref="XmlWriter"/>. Building it in memory (ClosedXML, or the Open XML SDK,
/// which keeps every part uncompressed until it closes) took 25 to 75 KB a row.
/// </summary>
public class VectorSheetExportService
{
    public const string VectorsSheetName = "Vectors";
    public const string AboutSheetName = "About";

    /// <summary>Excel's limits: 1,048,576 rows (one is the header), 16,384 columns, 32,767 characters a cell.</summary>
    private const int MaxDataRows = 1_048_575;
    private const int MaxColumns = 16_384;
    private const int MaxCellText = 32_767;

    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// The About sheet's Method line for an encoding. Loading a vectors workbook back checks it, since vectors made
    /// another way (a different n-gram length or hash size) can't be compared with this version's.
    /// </summary>
    public static string MethodDescription(string encodingMethod) => encodingMethod == VectorEncoding.Plain
        ? $"Character {VectorizationService.NgramLength}-grams hashed into 2^{VectorizationService.NumberOfBits} buckets, L2-normalized"
        : $"Character {VectorizationService.NgramLength}-grams and sounds-alike (Metaphone) codes, hashed with HMAC-SHA256 " +
          $"under the matching key into 2^{VectorizationService.NumberOfBits} buckets, L2-normalized";

    /// <summary>The bold style's index in the stylesheet this writes.</summary>
    private const int BoldStyle = 1;

    public Task ExportVectorsAsync(
        string outputPath,
        string datasetName,
        string sourceFilePath,
        List<Dictionary<string, string>> rows,
        List<float[]> vectors,
        List<string> vectorizedColumns,
        List<string> preservedColumns,
        IProgress<(int current, int total, string message)>? progress = null,
        VectorizeOptions? options = null,
        bool includeVectorizedText = false) => Task.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("No output path was supplied.", nameof(outputPath));
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(vectors);
        ArgumentNullException.ThrowIfNull(vectorizedColumns);
        if (rows.Count != vectors.Count)
            throw new ArgumentException($"{rows.Count} rows but {vectors.Count} vectors.", nameof(vectors));
        if (!string.IsNullOrWhiteSpace(sourceFilePath)
            && string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(sourceFilePath),
                OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Choose a different file for the vectors: that one is the workbook being read.");
        preservedColumns ??= new List<string>();
        options ??= VectorizeOptions.None;
        var encoding = options.Encoding(vectorizedColumns);

        int dimensions = vectors.Count > 0 ? vectors[0].Length : VectorizationService.VectorDimensions;
        // 1-based: Row, the preserved columns, the text if asked for, then the vectors.
        int textColumn = includeVectorizedText ? 2 + preservedColumns.Count : 0;
        int firstVectorColumn = 2 + preservedColumns.Count + (includeVectorizedText ? 1 : 0);
        int lastColumn = firstVectorColumn + dimensions - 1;
        if (rows.Count > MaxDataRows)
            throw new InvalidOperationException($"{rows.Count:N0} rows is more than a worksheet holds ({MaxDataRows:N0}).");
        if (lastColumn > MaxColumns)
            throw new InvalidOperationException(
                $"{preservedColumns.Count} preserved columns plus {dimensions} vector columns is more than a worksheet holds ({MaxColumns:N0}); preserve fewer columns.");

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Written to a temporary file first, so a failure part-way never leaves a broken workbook behind.
        var temporary = outputPath + ".partial";
        try
        {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                WriteText(zip, "[Content_Types].xml", ContentTypes);
                WriteText(zip, "_rels/.rels", PackageRelationships);
                WriteText(zip, "xl/workbook.xml", Workbook);
                WriteText(zip, "xl/_rels/workbook.xml.rels", WorkbookRelationships);
                WriteText(zip, "xl/styles.xml", Styles);
                WriteXml(zip, "xl/worksheets/sheet1.xml", writer => WriteVectors(writer, rows, vectors,
                    vectorizedColumns, preservedColumns, dimensions, textColumn, firstVectorColumn, options, progress));
                WriteXml(zip, "xl/worksheets/sheet2.xml", writer => WriteAbout(writer, datasetName, sourceFilePath,
                    rows.Count, dimensions, vectorizedColumns, preservedColumns, firstVectorColumn, encoding,
                    includeVectorizedText));
                progress?.Report((rows.Count, rows.Count, "Saving the spreadsheet..."));
            }
            File.Move(temporary, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    });

    // ── Sheets ────────────────────────────────────────────────────────────

    private static void WriteVectors(
        XmlWriter w, List<Dictionary<string, string>> rows, List<float[]> vectors, List<string> vectorizedColumns,
        List<string> preservedColumns, int dimensions, int textColumn, int firstVectorColumn, VectorizeOptions options,
        IProgress<(int current, int total, string message)>? progress)
    {
        bool withText = textColumn > 0;
        var texts = withText
            ? rows.Select(row => VectorizationService.DescribeInput(row, vectorizedColumns, options)).ToList()
            : new List<string>();

        // Widths for the readable columns, from their header and first few hundred values.
        var sample = Math.Min(rows.Count, 300);
        var widths = new List<double> { 7 };
        widths.AddRange(preservedColumns.Select(c =>
            Width(rows.Take(sample).Select(r => r.GetValueOrDefault(c, string.Empty).Length).Append(c.Length).Max())));
        if (withText)
            widths.Add(Width(texts.Take(sample).Select(t => t.Length).Append(15).Max()));

        w.WriteStartElement("worksheet", Main);

        // Freeze the header row and the readable columns, so the vectors scroll under them.
        w.WriteStartElement("sheetViews");
        w.WriteStartElement("sheetView");
        w.WriteAttributeString("workbookViewId", "0");
        w.WriteStartElement("pane");
        w.WriteAttributeString("xSplit", (firstVectorColumn - 1).ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("ySplit", "1");
        w.WriteAttributeString("topLeftCell", $"{ColumnName(firstVectorColumn)}2");
        w.WriteAttributeString("activePane", "bottomRight");
        w.WriteAttributeString("state", "frozen");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();

        WriteColumns(w, widths);
        w.WriteStartElement("sheetData");

        w.WriteStartElement("row");
        w.WriteAttributeString("r", "1");
        TextCell(w, "Row", BoldStyle);
        foreach (var column in preservedColumns)
            TextCell(w, column, BoldStyle);
        if (withText)
            TextCell(w, "Vectorized Text", BoldStyle);
        for (int d = 0; d < dimensions; d++)
            TextCell(w, $"V{d}", BoldStyle);
        w.WriteEndElement();

        // Zeros are written as 0, not left blank, so every reader sees a complete vector.
        for (int r = 0; r < rows.Count; r++)
        {
            if (r % 500 == 0 || r == rows.Count - 1)
                progress?.Report((r + 1, rows.Count, $"Writing row {r + 1:N0} of {rows.Count:N0} to the spreadsheet..."));

            w.WriteStartElement("row");
            w.WriteAttributeString("r", (r + 2).ToString(CultureInfo.InvariantCulture));
            NumberCell(w, (r + 1).ToString(CultureInfo.InvariantCulture));
            foreach (var column in preservedColumns)
                TextCell(w, rows[r].GetValueOrDefault(column, string.Empty));
            if (withText)
                TextCell(w, texts[r]);
            var vector = vectors[r];
            for (int d = 0; d < dimensions; d++)
                NumberCell(w, d < vector.Length && vector[d] != 0 ? vector[d].ToString("R", CultureInfo.InvariantCulture) : "0");
            w.WriteEndElement();
        }

        w.WriteEndElement();   // sheetData
        w.WriteEndElement();   // worksheet
    }

    private static void WriteAbout(
        XmlWriter w, string datasetName, string sourceFilePath, int rowCount, int dimensions,
        List<string> vectorizedColumns, List<string> preservedColumns, int firstVectorColumn, VectorEncoding encoding,
        bool includesText)
    {
        var lines = new (string Label, string Value)[]
        {
            ("Dataset", (datasetName ?? string.Empty).Trim()),
            ("Source file", Path.GetFileName(sourceFilePath ?? string.Empty)),
            ("Created (UTC)", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            ("Rows", rowCount.ToString("N0", CultureInfo.InvariantCulture)),
            ("Vectorized columns", string.Join(", ", vectorizedColumns)),
            ("Preserved columns", string.Join(", ", preservedColumns)),
            ("Vector dimensions", dimensions.ToString(CultureInfo.InvariantCulture)),
            ("Vector columns", $"V0 to V{dimensions - 1}, starting at column {ColumnName(firstVectorColumn)} of the {VectorsSheetName} sheet"),
            ("Method", MethodDescription(encoding.Method)),
            ("Encoding", encoding.Method),
            ("Key fingerprint", encoding.KeyFingerprint ?? ""),
            ("Sounds-alike columns", string.Join(", ", encoding.SoundsAlikeColumns)),
            ("Similarity", "Vectors have length 1, so the cosine similarity of two rows is the sum of their products (SUMPRODUCT)"),
            ("Privacy", encoding.HasKey
                ? "Vectorized with a matching key: without it the vectors can't be rebuilt from guesses. Preserved columns" +
                  (includesText ? " and the Vectorized Text column" : "") + " are readable as they are."
                : "No matching key: anyone with Vector Match Engine can vectorize guesses (names, dates) and compare, so " +
                  "these vectors don't hide what they were made from. Preserved columns" +
                  (includesText ? " and the Vectorized Text column" : "") + " are readable as they are."),
        };

        w.WriteStartElement("worksheet", Main);
        WriteColumns(w, [22, Width(lines.Max(l => l.Value.Length))]);
        w.WriteStartElement("sheetData");
        for (int i = 0; i < lines.Length; i++)
        {
            w.WriteStartElement("row");
            w.WriteAttributeString("r", (i + 1).ToString(CultureInfo.InvariantCulture));
            TextCell(w, lines[i].Label, BoldStyle);
            TextCell(w, lines[i].Value);
            w.WriteEndElement();
        }
        w.WriteEndElement();
        w.WriteEndElement();
    }

    // ── Cells ─────────────────────────────────────────────────────────────

    private static void WriteColumns(XmlWriter w, IReadOnlyList<double> widths)
    {
        w.WriteStartElement("cols");
        for (int i = 0; i < widths.Count; i++)
        {
            var n = (i + 1).ToString(CultureInfo.InvariantCulture);
            w.WriteStartElement("col");
            w.WriteAttributeString("min", n);
            w.WriteAttributeString("max", n);
            w.WriteAttributeString("width", widths[i].ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("customWidth", "1");
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    private static void TextCell(XmlWriter w, string text, int style = 0)
    {
        w.WriteStartElement("c");
        w.WriteAttributeString("t", "inlineStr");
        if (style != 0)
            w.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
        w.WriteStartElement("is");
        w.WriteStartElement("t");
        w.WriteAttributeString("xml", "space", null, "preserve");
        w.WriteString(XmlSafe(text));
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void NumberCell(XmlWriter w, string invariantNumber)
    {
        w.WriteStartElement("c");
        w.WriteElementString("v", invariantNumber);
        w.WriteEndElement();
    }

    /// <summary>Drops characters XML can't carry (control characters, lone surrogates) and keeps within a cell's limit.</summary>
    internal static string XmlSafe(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        if (text.Length > MaxCellText)
            text = text[..MaxCellText];

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    sb.Append(c).Append(text[++i]);
            }
            else if (c == '\t' || c == '\n' || c == '\r' || (c >= 0x20 && c <= 0xD7FF) || (c >= 0xE000 && c <= 0xFFFD))
            {
                sb.Append(c);
            }
        }
        return sb.Length == text.Length ? text : sb.ToString();
    }

    private static double Width(int characters) => Math.Clamp(characters + 2, 6, 60);

    /// <summary>A 1-based column number as letters: 1 is A, 27 is AA.</summary>
    internal static string ColumnName(int column)
    {
        var name = string.Empty;
        for (; column > 0; column = (column - 1) / 26)
            name = (char)('A' + (column - 1) % 26) + name;
        return name;
    }

    // ── Package parts ─────────────────────────────────────────────────────

    private static void WriteText(ZipArchive zip, string name, string content)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void WriteXml(ZipArchive zip, string name, Action<XmlWriter> write)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
        writer.WriteStartDocument(standalone: true);
        write(writer);
        writer.WriteEndDocument();
    }

    private const string Declaration = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";

    private const string ContentTypes = Declaration +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "</Types>";

    private const string PackageRelationships = Declaration +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private const string Workbook = Declaration +
        "<workbook xmlns=\"" + Main + "\" xmlns:r=\"" + RelationshipsNs + "\"><sheets>" +
        "<sheet name=\"" + VectorsSheetName + "\" sheetId=\"1\" r:id=\"rId1\"/>" +
        "<sheet name=\"" + AboutSheetName + "\" sheetId=\"2\" r:id=\"rId2\"/>" +
        "</sheets></workbook>";

    private const string WorkbookRelationships = Declaration +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"" + RelationshipsNs + "/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"" + RelationshipsNs + "/worksheet\" Target=\"worksheets/sheet2.xml\"/>" +
        "<Relationship Id=\"rId3\" Type=\"" + RelationshipsNs + "/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";

    /// <summary>The smallest stylesheet Excel accepts, plus one bold style (index 1).</summary>
    private const string Styles = Declaration +
        "<styleSheet xmlns=\"" + Main + "\">" +
        "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
        "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
        "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";
}
