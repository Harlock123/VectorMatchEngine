using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VectorMatchEngine.Core.Models;
using VectorMatchEngine.Core.Services;
using VectorMatchEngine.Data.Entities;
using VectorMatchEngine.Data.Repositories;

namespace VectorMatchEngine.Data;

/// <summary>High-level orchestration service; the single entry point the UI calls.</summary>
public class DataService
{
    /// <summary>MatchResult rows are saved in batches of this size to bound memory and round-trips.</summary>
    private const int InsertBatchSize = 2000;

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IDatasetRepository _datasetRepository;
    private readonly IMatchJobRepository _matchJobRepository;
    private readonly ExcelService _excelService;
    private readonly VectorizationService _vectorizationService;
    private readonly SimilarityService _similarityService;
    private readonly ExcelExportService _excelExportService;
    private readonly VectorSheetExportService _vectorSheetExportService;
    private readonly VectorSheetImportService _vectorSheetImportService;
    private readonly SqlVectorStore _sqlVectorStore;
    private readonly ILogger<DataService> _logger;

    public DataService(
        IDbContextFactory<AppDbContext> contextFactory,
        IDatasetRepository datasetRepository,
        IMatchJobRepository matchJobRepository,
        ExcelService excelService,
        VectorizationService vectorizationService,
        SimilarityService similarityService,
        ExcelExportService excelExportService,
        VectorSheetExportService vectorSheetExportService,
        VectorSheetImportService vectorSheetImportService,
        SqlVectorStore sqlVectorStore,
        ILogger<DataService>? logger = null)
    {
        _contextFactory = contextFactory;
        _datasetRepository = datasetRepository;
        _matchJobRepository = matchJobRepository;
        _excelService = excelService;
        _vectorizationService = vectorizationService;
        _similarityService = similarityService;
        _excelExportService = excelExportService;
        _vectorSheetExportService = vectorSheetExportService;
        _vectorSheetImportService = vectorSheetImportService;
        _sqlVectorStore = sqlVectorStore;
        _logger = logger ?? NullLogger<DataService>.Instance;
    }

    // ── Schema ────────────────────────────────────────────────────────────

    /// <summary>Applies any pending EF Core migrations, creating the database if needed.</summary>
    public async Task MigrateAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
    }

    /// <summary>Opens a connection to verify the configured connection string works.</summary>
    public async Task TestConnectionAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Database.OpenConnectionAsync();
        await context.Database.CloseConnectionAsync();
    }

    /// <summary>
    /// Whether the configured server can do the comparing itself with its VECTOR type (SQL Server 2025, Azure SQL).
    /// </summary>
    public Task<bool> SupportsDatabaseVectorsAsync() => _sqlVectorStore.IsSupportedAsync();

    // ── Column discovery ──────────────────────────────────────────────────

    public Task<List<string>> GetColumnNamesAsync(string filePath)
        => _excelService.GetColumnNamesAsync(filePath);

    // ── Ingestion ─────────────────────────────────────────────────────────

    public async Task<Dataset> IngestExcelAsync(
        string filePath,
        string datasetName,
        List<string> vectorizedColumns,
        List<string> preservedColumns,
        IProgress<(int current, int total, string message)>? progress = null,
        VectorizeOptions? options = null)
    {
        preservedColumns ??= new List<string>();
        options ??= VectorizeOptions.None;
        var (rows, vectors, fieldVectors) = await ReadAndVectorizeAsync(filePath, datasetName, vectorizedColumns, options, progress);

        var preserved = rows
            .Select(row => preservedColumns.ToDictionary(
                column => column,
                column => row.GetValueOrDefault(column, string.Empty)))
            .ToList();

        return await SaveDatasetAsync(
            datasetName, Path.GetFileName(filePath), vectorizedColumns, preservedColumns, preserved, vectors,
            fieldVectors, options.Encoding(vectorizedColumns), progress);
    }

    /// <summary>
    /// What a workbook holds if it's a vectors workbook (one written by <see cref="VectorizeToSpreadsheetAsync"/>);
    /// null for any other workbook. Throws if it is one but its vectors don't fit this version.
    /// </summary>
    public Task<VectorWorkbookInfo?> InspectVectorWorkbookAsync(string filePath)
        => _vectorSheetImportService.InspectAsync(filePath);

    /// <summary>
    /// Loads a vectors workbook into the database as a dataset, using its vectors as they are (no vectorizing),
    /// so it can be matched like any imported dataset.
    /// </summary>
    public async Task<Dataset> LoadVectorWorkbookAsync(
        string filePath,
        string datasetName,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(datasetName))
            throw new ArgumentException("A dataset name is required.", nameof(datasetName));

        progress?.Report((0, 0, "Reading the vectors workbook..."));
        var workbook = await _vectorSheetImportService.ReadAsync(filePath, progress);
        var info = workbook.Info;

        // The dataset is named after the data's own source file when the workbook records it.
        var fileName = info.SourceFile.Length > 0
            ? $"{info.SourceFile} (via {Path.GetFileName(filePath)})"
            : Path.GetFileName(filePath);
        if (fileName.Length > 512)
            fileName = fileName[..512];

        return await SaveDatasetAsync(
            datasetName, fileName, info.VectorizedColumns, info.PreservedColumns, workbook.Preserved, workbook.Vectors,
            workbook.FieldVectors, info.Encoding, progress);
    }

    /// <summary>Stores a dataset: one record per row, with its vector, field vectors (if any) and preserved values.</summary>
    private async Task<Dataset> SaveDatasetAsync(
        string datasetName,
        string fileName,
        List<string> vectorizedColumns,
        List<string> preservedColumns,
        List<Dictionary<string, string>> preserved,
        List<float[]> vectors,
        List<SparseVector[]>? fieldVectors,
        VectorEncoding encoding,
        IProgress<(int current, int total, string message)>? progress)
    {
        int count = vectors.Count;
        var dataset = new Dataset
        {
            Name = datasetName.Trim(),
            FileName = fileName,
            RowCount = count,
            VectorizedColumnsJson = JsonSerializer.Serialize(vectorizedColumns),
            PreservedColumnsJson = JsonSerializer.Serialize(preservedColumns),
            VectorDimensions = count > 0 ? vectors[0].Length : 0,
            CreatedAt = DateTime.UtcNow,
            Encoding = encoding.Method,
            KeyFingerprint = encoding.KeyFingerprint,
            SoundsAlikeColumnsJson = JsonSerializer.Serialize(encoding.SoundsAlikeColumns),
            Cleanup = encoding.Cleanup,
            FieldKindsJson = JsonSerializer.Serialize(encoding.Kinds.ToDictionary(k => k.Key, k => k.Value.ToString())),
            HasFieldVectors = fieldVectors is not null && fieldVectors.Count == count
        };

        var records = new List<DatasetRecord>(count);
        for (int i = 0; i < count; i++)
        {
            // Reporting every row would swamp the dispatcher on large files.
            if (i % 100 == 0 || i == count - 1)
                progress?.Report((i + 1, count, $"Preparing record {i + 1:N0} of {count:N0}..."));

            records.Add(new DatasetRecord
            {
                RowIndex = i,
                VectorData = _vectorizationService.SerializeVector(vectors[i]),
                PreservedDataJson = JsonSerializer.Serialize(preserved[i]),
                FieldVectorData = dataset.HasFieldVectors ? FieldVectorCodec.Serialize(fieldVectors![i]) : null
            });
        }

        dataset.Records = records;

        progress?.Report((count, count, $"Saving {count:N0} records to the database..."));
        await _datasetRepository.CreateAsync(dataset);

        progress?.Report((count, count, "Done."));
        _logger.LogInformation(
            "Saved dataset '{Name}' ({RowCount} rows, {Dimensions} dims).",
            dataset.Name, dataset.RowCount, dataset.VectorDimensions);

        return dataset;
    }

    /// <summary>
    /// Vectorizes a workbook like <see cref="IngestExcelAsync"/>, but writes the vectors to another .xlsx file
    /// (see <see cref="VectorSheetExportService"/>) instead of the database. Needs no database connection.
    /// Returns the number of rows written.
    /// </summary>
    public async Task<int> VectorizeToSpreadsheetAsync(
        string filePath,
        string datasetName,
        List<string> vectorizedColumns,
        List<string> preservedColumns,
        string outputPath,
        IProgress<(int current, int total, string message)>? progress = null,
        VectorizeOptions? options = null,
        bool includeVectorizedText = false)
    {
        preservedColumns ??= new List<string>();
        options ??= VectorizeOptions.None;
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Choose a file to write the vectors to.", nameof(outputPath));

        var (rows, vectors, fieldVectors) = await ReadAndVectorizeAsync(filePath, datasetName, vectorizedColumns, options, progress);
        await _vectorSheetExportService.ExportVectorsAsync(
            outputPath, datasetName, filePath, rows, vectors, vectorizedColumns, preservedColumns, progress,
            options, includeVectorizedText, fieldVectors);

        progress?.Report((rows.Count, rows.Count, "Done."));
        _logger.LogInformation(
            "Wrote {RowCount} vectors for '{Name}' to {OutputPath}.", rows.Count, datasetName, outputPath);
        return rows.Count;
    }

    /// <summary>Checks the request, reads the workbook's rows and vectorizes them (one vector per row, and one per field).</summary>
    private async Task<(List<Dictionary<string, string>> Rows, List<float[]> Vectors, List<SparseVector[]> FieldVectors)> ReadAndVectorizeAsync(
        string filePath,
        string datasetName,
        List<string> vectorizedColumns,
        VectorizeOptions options,
        IProgress<(int current, int total, string message)>? progress)
    {
        ArgumentNullException.ThrowIfNull(vectorizedColumns);
        if (string.IsNullOrWhiteSpace(datasetName))
            throw new ArgumentException("A dataset name is required.", nameof(datasetName));
        if (vectorizedColumns.Count == 0)
            throw new ArgumentException("Select at least one column to vectorize.", nameof(vectorizedColumns));

        progress?.Report((0, 0, "Reading Excel file..."));
        var rows = await _excelService.ReadRowsAsync(filePath);

        if (rows.Count == 0)
            throw new InvalidOperationException("The worksheet contains a header row but no data rows.");

        progress?.Report((0, rows.Count, $"Vectorizing {rows.Count:N0} rows..."));
        var vectors = _vectorizationService.VectorizeRows(rows, vectorizedColumns, options);
        var fieldVectors = _vectorizationService.VectorizeFields(rows, vectorizedColumns, options);
        return (rows, vectors, fieldVectors);
    }

    // ── Dataset listing ───────────────────────────────────────────────────

    public async Task<List<DatasetInfo>> GetDatasetsAsync()
    {
        var datasets = await _datasetRepository.GetAllAsync();
        return datasets.Select(ToDatasetInfo).ToList();
    }

    public Task DeleteDatasetAsync(int id) => _datasetRepository.DeleteAsync(id);

    // ── Matching ──────────────────────────────────────────────────────────

    public async Task<MatchJob> RunMatchJobAsync(MatchJobRequest request, IProgress<int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.DatasetAId == request.DatasetBId)
            _logger.LogWarning("Match job is comparing dataset {Id} against itself.", request.DatasetAId);

        var job = await _matchJobRepository.CreateAsync(new MatchJob
        {
            DatasetAId = request.DatasetAId,
            DatasetBId = request.DatasetBId,
            Threshold = request.SimilarityThreshold,
            Status = "Running",
            CreatedAt = DateTime.UtcNow,
            SettingsJson = JsonSerializer.Serialize(request.Settings)
        });

        try
        {
            await CheckEncodingsMatchAsync(request.DatasetAId, request.DatasetBId, request.Settings);

            int matchCount = request.UseDatabaseVectors && !request.Settings.NeedsAppMatching
                             && await _sqlVectorStore.IsSupportedAsync()
                ? await MatchInDatabaseAsync(job, request, progress)
                : await MatchInAppAsync(job, request, progress);

            job.Status = "Completed";
            job.TotalMatchesFound = matchCount;
            job.CompletedAt = DateTime.UtcNow;
            await _matchJobRepository.UpdateAsync(job);

            _logger.LogInformation(
                "Match job {JobId} completed {Where}: {MatchCount} matches at threshold {Threshold}.",
                job.Id, job.MatchedInDatabase ? "in SQL Server" : "in the app", matchCount, request.SimilarityThreshold);

            return job;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Match job {JobId} failed.", job.Id);

            // Results saved before the failure would make a failed job look partly done.
            try { await DeleteMatchResultsAsync(job.Id); }
            catch (Exception cleanupEx) { _logger.LogError(cleanupEx, "Could not clear job {JobId}'s partial results.", job.Id); }

            job.Status = "Failed";
            job.ErrorMessage = ex.Message;
            job.CompletedAt = DateTime.UtcNow;

            // A failure recording the failure must not mask the original exception.
            try { await _matchJobRepository.UpdateAsync(job); }
            catch (Exception updateEx) { _logger.LogError(updateEx, "Could not mark job {JobId} as failed.", job.Id); }

            throw;
        }
    }

    /// <summary>Loads both datasets and compares every pair in this process (SimilarityService), then saves the matches.</summary>
    private async Task<int> MatchInAppAsync(MatchJob job, MatchJobRequest request, IProgress<int>? progress)
    {
        var datasetA = await LoadRecordsAsync(request.DatasetAId);
        var datasetB = await LoadRecordsAsync(request.DatasetBId);

        if (datasetA.Count == 0 || datasetB.Count == 0)
            throw new InvalidOperationException("One of the selected datasets has no records.");

        var dimensionsA = datasetA[0].Vector.Length;
        var dimensionsB = datasetB[0].Vector.Length;
        if (dimensionsA != dimensionsB)
        {
            throw new InvalidOperationException(
                $"Vector dimensions differ ({dimensionsA} vs {dimensionsB}). Both datasets must be " +
                "imported with the same version of the vectorizer; re-import the older one.");
        }

        var run = await _similarityService.FindMatchesAsync(
            datasetA, datasetB, request.SimilarityThreshold, request.Settings, progress);

        job.PairsCompared = run.PairsCompared;
        await SaveMatchResultsAsync(job.Id, run.Pairs);
        return run.Pairs.Count;
    }

    /// <summary>Has SQL Server score the pairs with VECTOR_DISTANCE and write the matches itself (SqlVectorStore).</summary>
    private async Task<int> MatchInDatabaseAsync(MatchJob job, MatchJobRequest request, IProgress<int>? progress)
    {
        await using (var context = await _contextFactory.CreateDbContextAsync())
        {
            var datasets = await context.Datasets.AsNoTracking()
                .Where(d => d.Id == request.DatasetAId || d.Id == request.DatasetBId)
                .Select(d => new { d.RowCount, d.VectorDimensions })
                .ToListAsync();

            if (datasets.Any(d => d.RowCount == 0))
                throw new InvalidOperationException("One of the selected datasets has no records.");

            // The VECTOR column has one fixed size; a dataset from another vectorizer version can't go in it.
            if (datasets.FirstOrDefault(d => d.VectorDimensions != VectorizationService.VectorDimensions) is { } odd)
                throw new InvalidOperationException(
                    $"Vector dimensions differ ({odd.VectorDimensions} vs {VectorizationService.VectorDimensions}). " +
                    "Both datasets must be imported with the same version of the vectorizer; re-import the older one.");
        }

        job.MatchedInDatabase = true;
        return await _sqlVectorStore.MatchIntoResultsAsync(
            job.Id, request.DatasetAId, request.DatasetBId, request.SimilarityThreshold, progress);
    }

    private async Task DeleteMatchResultsAsync(int matchJobId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.MatchResults.Where(r => r.MatchJobId == matchJobId).ExecuteDeleteAsync();
    }

    /// <summary>
    /// Refuses to match datasets whose vectors were made differently (their scores would mean nothing), and checks
    /// the field pairing: the fields exist, both datasets have field vectors, and each pair is encoded the same way.
    /// Sets each pairing's indices from its column names.
    /// </summary>
    private async Task CheckEncodingsMatchAsync(int datasetAId, int datasetBId, MatchSettings settings)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var datasets = await context.Datasets.AsNoTracking()
            .Where(d => d.Id == datasetAId || d.Id == datasetBId)
            .ToListAsync();
        var a = datasets.FirstOrDefault(d => d.Id == datasetAId);
        var b = datasets.FirstOrDefault(d => d.Id == datasetBId);
        if (a is null || b is null)
            throw new InvalidOperationException("One of the selected datasets no longer exists.");
        if (EncodingOf(a).IncompatibilityWith(EncodingOf(b)) is { } reason)
            throw new InvalidOperationException(
                $"'{a.Name}' and '{b.Name}' can't be matched: {reason}. Import them again the same way " +
                "(the same matching key, sounds-alike and field type settings).");

        if (settings.Fields.Count == 0)
            return;
        if (!a.HasFieldVectors || !b.HasFieldVectors)
            throw new InvalidOperationException(
                $"'{(a.HasFieldVectors ? b : a).Name}' was imported before field vectors, so it can't be scored or " +
                "explained field by field. Import it again, or match without paired fields.");

        var encodingA = EncodingOf(a);
        var encodingB = EncodingOf(b);
        var columnsA = DeserializeColumns(a.VectorizedColumnsJson);
        var columnsB = DeserializeColumns(b.VectorizedColumnsJson);
        foreach (var field in settings.Fields)
        {
            field.IndexA = columnsA.IndexOf(field.NameA);
            field.IndexB = columnsB.IndexOf(field.NameB);
            if (field.IndexA < 0 || field.IndexB < 0)
                throw new InvalidOperationException(
                    $"'{(field.IndexA < 0 ? field.NameA : field.NameB)}' isn't one of '{(field.IndexA < 0 ? a.Name : b.Name)}''s vectorized columns.");
            bool soundA = encodingA.SoundsAlikeColumns.Contains(field.NameA);
            bool soundB = encodingB.SoundsAlikeColumns.Contains(field.NameB);
            if (soundA != soundB)
                throw new InvalidOperationException(
                    $"{field.Label} can't be paired: it's encoded by sound in '{(soundA ? a.Name : b.Name)}' but spelled " +
                    $"in '{(soundA ? b.Name : a.Name)}'.");
            var kindA = encodingA.KindOf(field.NameA);
            var kindB = encodingB.KindOf(field.NameB);
            if (kindA != kindB)
                throw new InvalidOperationException(
                    $"{field.Label} can't be paired: its field type is {FieldKinds.Label(kindA)} in '{a.Name}' but " +
                    $"{FieldKinds.Label(kindB)} in '{b.Name}'.");
        }
    }

    /// <summary>A job's settings; the defaults (whole row, every pair) for jobs from before settings were saved.</summary>
    public static MatchSettings SettingsOf(MatchJob job)
    {
        if (string.IsNullOrWhiteSpace(job.SettingsJson))
            return new MatchSettings();
        try { return JsonSerializer.Deserialize<MatchSettings>(job.SettingsJson) ?? new MatchSettings(); }
        catch (JsonException) { return new MatchSettings(); }
    }

    /// <summary>How a dataset's vectors were made.</summary>
    public static VectorEncoding EncodingOf(Dataset dataset) => new(
        string.IsNullOrEmpty(dataset.Encoding) ? VectorEncoding.Plain : dataset.Encoding,
        string.IsNullOrEmpty(dataset.KeyFingerprint) ? null : dataset.KeyFingerprint,
        DeserializeColumns(dataset.SoundsAlikeColumnsJson),
        string.IsNullOrEmpty(dataset.Cleanup) ? null : dataset.Cleanup,
        DeserializeKinds(dataset.FieldKindsJson));

    private static Dictionary<string, FieldKind> DeserializeKinds(string? json)
    {
        var kinds = new Dictionary<string, FieldKind>();
        if (string.IsNullOrWhiteSpace(json))
            return kinds;
        try
        {
            foreach (var (column, name) in JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new())
                if (FieldKinds.Parse(name) is { } kind && kind != FieldKind.Text)
                    kinds[column] = kind;
        }
        catch (JsonException) { }
        return kinds;
    }

    /// <summary>Loads a dataset's records for the matcher (vector, field vectors, preserved values), without navigations.</summary>
    private async Task<List<MatchRecord>> LoadRecordsAsync(int datasetId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var raw = await context.DatasetRecords
            .AsNoTracking()
            .Where(r => r.DatasetId == datasetId)
            .OrderBy(r => r.RowIndex)
            .Select(r => new { r.Id, r.VectorData, r.FieldVectorData, r.PreservedDataJson })
            .ToListAsync();

        var result = new List<MatchRecord>(raw.Count);
        foreach (var record in raw)
        {
            result.Add(new MatchRecord(
                record.Id,
                _vectorizationService.DeserializeVector(record.VectorData),
                FieldVectorCodec.Deserialize(record.FieldVectorData),
                DeserializePreserved(record.PreservedDataJson)));
        }

        return result;
    }

    private async Task SaveMatchResultsAsync(int matchJobId, List<MatchedPair> pairs)
    {
        if (pairs.Count == 0)
            return;

        await using var context = await _contextFactory.CreateDbContextAsync();
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        for (int offset = 0; offset < pairs.Count; offset += InsertBatchSize)
        {
            var batch = pairs.Skip(offset).Take(InsertBatchSize).Select(pair => new MatchResult
            {
                MatchJobId = matchJobId,
                RecordAId = pair.RecordAId,
                RecordBId = pair.RecordBId,
                SimilarityScore = pair.SimilarityScore,
                FieldScores = FormatFieldScores(pair.FieldScores),
                RunnerUpScore = pair.RunnerUpScore
            });

            context.MatchResults.AddRange(batch);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }
    }

    // ── Results retrieval ─────────────────────────────────────────────────

    public async Task<List<MatchedPair>> GetMatchResultsAsync(int matchJobId)
    {
        var job = await _matchJobRepository.GetWithResultsAsync(matchJobId);
        if (job is null)
            return new List<MatchedPair>();

        return job.Results
            .OrderByDescending(result => result.SimilarityScore)
            .Select(result => new MatchedPair
            {
                RecordAId = result.RecordAId,
                RecordBId = result.RecordBId,
                SimilarityScore = result.SimilarityScore,
                FieldScores = ParseFieldScores(result.FieldScores),
                RunnerUpScore = result.RunnerUpScore,
                DatasetAPreserved = DeserializePreserved(result.RecordA?.PreservedDataJson),
                DatasetBPreserved = DeserializePreserved(result.RecordB?.PreservedDataJson)
            })
            .ToList();
    }

    private static string? FormatFieldScores(double?[]? scores) => scores is null
        ? null
        : string.Join('|', scores.Select(s => s?.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) ?? ""));

    private static double?[]? ParseFieldScores(string? text) => string.IsNullOrEmpty(text)
        ? null
        : text.Split('|').Select(part => double.TryParse(part, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var score) ? score : (double?)null).ToArray();

    public Task<List<MatchJob>> GetMatchJobsAsync() => _matchJobRepository.GetAllAsync();

    public Task<MatchJob?> GetMatchJobAsync(int matchJobId) => _matchJobRepository.GetWithResultsAsync(matchJobId);

    public Task DeleteMatchJobAsync(int matchJobId) => _matchJobRepository.DeleteAsync(matchJobId);

    // ── Export ────────────────────────────────────────────────────────────

    public async Task ExportMatchResultsAsync(int matchJobId, string outputPath)
    {
        var pairs = await GetMatchResultsAsync(matchJobId);

        await using var context = await _contextFactory.CreateDbContextAsync();
        var job = await context.MatchJobs
            .AsNoTracking()
            .Include(j => j.DatasetA)
            .Include(j => j.DatasetB)
            .FirstOrDefaultAsync(j => j.Id == matchJobId)
            ?? throw new InvalidOperationException($"Match job {matchJobId} no longer exists.");

        var columnsA = DeserializeColumns(job.DatasetA?.PreservedColumnsJson);
        var columnsB = DeserializeColumns(job.DatasetB?.PreservedColumnsJson);

        var fieldLabels = SettingsOf(job).Fields.Select(f => f.Label).ToList();
        await _excelExportService.ExportMatchResultsAsync(outputPath, pairs, columnsA, columnsB, fieldLabels);
    }

    // ── Mapping helpers ───────────────────────────────────────────────────

    public static DatasetInfo ToDatasetInfo(Dataset dataset) => new()
    {
        Id = dataset.Id,
        Name = dataset.Name,
        FileName = dataset.FileName,
        RowCount = dataset.RowCount,
        VectorizedColumns = DeserializeColumns(dataset.VectorizedColumnsJson),
        PreservedColumns = DeserializeColumns(dataset.PreservedColumnsJson),
        VectorDimensions = dataset.VectorDimensions,
        CreatedAt = dataset.CreatedAt,
        Encoding = EncodingOf(dataset),
        HasFieldVectors = dataset.HasFieldVectors
    };

    public static List<string> DeserializeColumns(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
        catch (JsonException) { return new List<string>(); }
    }

    private static Dictionary<string, string> DeserializePreserved(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
