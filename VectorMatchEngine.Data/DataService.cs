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
    private readonly ILogger<DataService> _logger;

    public DataService(
        IDbContextFactory<AppDbContext> contextFactory,
        IDatasetRepository datasetRepository,
        IMatchJobRepository matchJobRepository,
        ExcelService excelService,
        VectorizationService vectorizationService,
        SimilarityService similarityService,
        ExcelExportService excelExportService,
        ILogger<DataService>? logger = null)
    {
        _contextFactory = contextFactory;
        _datasetRepository = datasetRepository;
        _matchJobRepository = matchJobRepository;
        _excelService = excelService;
        _vectorizationService = vectorizationService;
        _similarityService = similarityService;
        _excelExportService = excelExportService;
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
        IProgress<(int current, int total, string message)>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(vectorizedColumns);
        preservedColumns ??= new List<string>();

        if (string.IsNullOrWhiteSpace(datasetName))
            throw new ArgumentException("A dataset name is required.", nameof(datasetName));
        if (vectorizedColumns.Count == 0)
            throw new ArgumentException("Select at least one column to vectorize.", nameof(vectorizedColumns));

        progress?.Report((0, 0, "Reading Excel file..."));
        var rows = await _excelService.ReadRowsAsync(filePath);

        if (rows.Count == 0)
            throw new InvalidOperationException("The worksheet contains a header row but no data rows.");

        progress?.Report((0, rows.Count, $"Vectorizing {rows.Count:N0} rows..."));
        var vectors = _vectorizationService.VectorizeRows(rows, vectorizedColumns);

        var dataset = new Dataset
        {
            Name = datasetName.Trim(),
            FileName = Path.GetFileName(filePath),
            RowCount = rows.Count,
            VectorizedColumnsJson = JsonSerializer.Serialize(vectorizedColumns),
            PreservedColumnsJson = JsonSerializer.Serialize(preservedColumns),
            VectorDimensions = vectors.Count > 0 ? vectors[0].Length : 0,
            CreatedAt = DateTime.UtcNow
        };

        var records = new List<DatasetRecord>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            // Reporting every row would swamp the dispatcher on large files.
            if (i % 100 == 0 || i == rows.Count - 1)
                progress?.Report((i + 1, rows.Count, $"Preparing record {i + 1:N0} of {rows.Count:N0}..."));

            var preserved = preservedColumns.ToDictionary(
                column => column,
                column => rows[i].GetValueOrDefault(column, string.Empty));

            records.Add(new DatasetRecord
            {
                RowIndex = i,
                VectorData = _vectorizationService.SerializeVector(vectors[i]),
                PreservedDataJson = JsonSerializer.Serialize(preserved)
            });
        }

        dataset.Records = records;

        progress?.Report((rows.Count, rows.Count, $"Saving {rows.Count:N0} records to the database..."));
        await _datasetRepository.CreateAsync(dataset);

        progress?.Report((rows.Count, rows.Count, "Done."));
        _logger.LogInformation(
            "Ingested dataset '{Name}' ({RowCount} rows, {Dimensions} dims).",
            dataset.Name, dataset.RowCount, dataset.VectorDimensions);

        return dataset;
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
        CreatedAt = dataset.CreatedAt
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
