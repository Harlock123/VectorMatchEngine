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
        var (rows, vectors) = await ReadAndVectorizeAsync(filePath, datasetName, vectorizedColumns, options, progress);

        var preserved = rows
            .Select(row => preservedColumns.ToDictionary(
                column => column,
                column => row.GetValueOrDefault(column, string.Empty)))
            .ToList();

        return await SaveDatasetAsync(
            datasetName, Path.GetFileName(filePath), vectorizedColumns, preservedColumns, preserved, vectors,
            options.Encoding(vectorizedColumns), progress);
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
            info.Encoding, progress);
    }

    /// <summary>Stores a dataset: one record per row, with its vector and preserved values.</summary>
    private async Task<Dataset> SaveDatasetAsync(
        string datasetName,
        string fileName,
        List<string> vectorizedColumns,
        List<string> preservedColumns,
        List<Dictionary<string, string>> preserved,
        List<float[]> vectors,
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
            SoundsAlikeColumnsJson = JsonSerializer.Serialize(encoding.SoundsAlikeColumns)
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
                PreservedDataJson = JsonSerializer.Serialize(preserved[i])
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

        var (rows, vectors) = await ReadAndVectorizeAsync(filePath, datasetName, vectorizedColumns, options, progress);
        await _vectorSheetExportService.ExportVectorsAsync(
            outputPath, datasetName, filePath, rows, vectors, vectorizedColumns, preservedColumns, progress,
            options, includeVectorizedText);

        progress?.Report((rows.Count, rows.Count, "Done."));
        _logger.LogInformation(
            "Wrote {RowCount} vectors for '{Name}' to {OutputPath}.", rows.Count, datasetName, outputPath);
        return rows.Count;
    }

    /// <summary>Checks the request, reads the workbook's rows and vectorizes them (one vector per row).</summary>
    private async Task<(List<Dictionary<string, string>> Rows, List<float[]> Vectors)> ReadAndVectorizeAsync(
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
        return (rows, vectors);
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
            CreatedAt = DateTime.UtcNow
        });

        try
        {
            await CheckEncodingsMatchAsync(request.DatasetAId, request.DatasetBId);
            var datasetA = await LoadVectorsAsync(request.DatasetAId);
            var datasetB = await LoadVectorsAsync(request.DatasetBId);

            if (datasetA.Count == 0 || datasetB.Count == 0)
                throw new InvalidOperationException("One of the selected datasets has no records.");

            var dimensionsA = datasetA[0].vector.Length;
            var dimensionsB = datasetB[0].vector.Length;
            if (dimensionsA != dimensionsB)
            {
                throw new InvalidOperationException(
                    $"Vector dimensions differ ({dimensionsA} vs {dimensionsB}). Both datasets must be " +
                    "imported with the same version of the vectorizer; re-import the older one.");
            }

            var pairs = await _similarityService.FindMatchesAsync(
                datasetA, datasetB, request.SimilarityThreshold, progress);

            await SaveMatchResultsAsync(job.Id, pairs);

            job.Status = "Completed";
            job.TotalMatchesFound = pairs.Count;
            job.CompletedAt = DateTime.UtcNow;
            await _matchJobRepository.UpdateAsync(job);

            _logger.LogInformation(
                "Match job {JobId} completed: {MatchCount} matches at threshold {Threshold}.",
                job.Id, pairs.Count, request.SimilarityThreshold);

            return job;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Match job {JobId} failed.", job.Id);

            job.Status = "Failed";
            job.ErrorMessage = ex.Message;
            job.CompletedAt = DateTime.UtcNow;

            // A failure recording the failure must not mask the original exception.
            try { await _matchJobRepository.UpdateAsync(job); }
            catch (Exception updateEx) { _logger.LogError(updateEx, "Could not mark job {JobId} as failed.", job.Id); }

            throw;
        }
    }

    /// <summary>Refuses to match datasets whose vectors were made differently: their scores would mean nothing.</summary>
    private async Task CheckEncodingsMatchAsync(int datasetAId, int datasetBId)
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
                "(the same matching key and sounds-alike settings).");
    }

    /// <summary>How a dataset's vectors were made.</summary>
    public static VectorEncoding EncodingOf(Dataset dataset) => new(
        string.IsNullOrEmpty(dataset.Encoding) ? VectorEncoding.Plain : dataset.Encoding,
        string.IsNullOrEmpty(dataset.KeyFingerprint) ? null : dataset.KeyFingerprint,
        DeserializeColumns(dataset.SoundsAlikeColumnsJson));

    /// <summary>Loads a dataset's records as (id, vector, preserved) tuples, without navigations.</summary>
    private async Task<List<(long id, float[] vector, Dictionary<string, string> preserved)>> LoadVectorsAsync(int datasetId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var raw = await context.DatasetRecords
            .AsNoTracking()
            .Where(r => r.DatasetId == datasetId)
            .OrderBy(r => r.RowIndex)
            .Select(r => new { r.Id, r.VectorData, r.PreservedDataJson })
            .ToListAsync();

        var result = new List<(long, float[], Dictionary<string, string>)>(raw.Count);
        foreach (var record in raw)
        {
            result.Add((
                record.Id,
                _vectorizationService.DeserializeVector(record.VectorData),
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
                SimilarityScore = pair.SimilarityScore
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
                DatasetAPreserved = DeserializePreserved(result.RecordA?.PreservedDataJson),
                DatasetBPreserved = DeserializePreserved(result.RecordB?.PreservedDataJson)
            })
            .ToList();
    }

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

        await _excelExportService.ExportMatchResultsAsync(outputPath, pairs, columnsA, columnsB);
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
        Encoding = EncodingOf(dataset)
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
