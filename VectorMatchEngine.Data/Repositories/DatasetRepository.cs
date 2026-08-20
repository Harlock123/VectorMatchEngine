using Microsoft.EntityFrameworkCore;
using VectorMatchEngine.Data.Entities;

namespace VectorMatchEngine.Data.Repositories;

/// <summary>
/// Uses IDbContextFactory rather than a shared AppDbContext: the repository is registered as a
/// singleton, so it must not capture a scoped context, and each call gets a context built from
/// whatever connection string is currently configured.
/// </summary>
public class DatasetRepository : IDatasetRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public DatasetRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<Dataset> CreateAsync(Dataset dataset)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Records are inserted in bulk with change tracking off; for large imports the
        // tracker's fixup cost otherwise dominates the insert.
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        context.Datasets.Add(dataset);
        await context.SaveChangesAsync();
        return dataset;
    }

    public async Task<List<Dataset>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Datasets
            .AsNoTracking()
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();
    }

    public async Task<Dataset?> GetAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Datasets.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<Dataset?> GetWithRecordsAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Datasets
            .AsNoTracking()
            .Include(d => d.Records)
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var dataset = await context.Datasets.FirstOrDefaultAsync(d => d.Id == id)
            ?? throw new InvalidOperationException($"Dataset {id} no longer exists.");

        // MatchJob -> Dataset is Restrict, so a referencing job would fail at the database
        // with an opaque FK error. Fail early with something the user can act on.
        var referencingJobs = await context.MatchJobs
            .AsNoTracking()
            .Where(j => j.DatasetAId == id || j.DatasetBId == id)
            .Select(j => j.Id)
            .ToListAsync();

        if (referencingJobs.Count > 0)
        {
            throw new InvalidOperationException(
                $"'{dataset.Name}' is used by {referencingJobs.Count} match job(s) " +
                $"(#{string.Join(", #", referencingJobs)}). Delete those match jobs first.");
        }

        context.Datasets.Remove(dataset);   // cascades to DatasetRecords
        await context.SaveChangesAsync();
    }
}
