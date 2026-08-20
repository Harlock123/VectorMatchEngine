using Microsoft.EntityFrameworkCore;
using VectorMatchEngine.Data.Entities;

namespace VectorMatchEngine.Data.Repositories;

public class MatchJobRepository : IMatchJobRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public MatchJobRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<MatchJob> CreateAsync(MatchJob job)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.MatchJobs.Add(job);
        await context.SaveChangesAsync();
        return job;
    }

    public async Task UpdateAsync(MatchJob job)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Update only the mutable job columns; the entity arrives detached from another context
        // and its navigation properties must not be re-inserted.
        var tracked = await context.MatchJobs.FirstOrDefaultAsync(j => j.Id == job.Id)
            ?? throw new InvalidOperationException($"Match job {job.Id} no longer exists.");

        tracked.Status = job.Status;
        tracked.TotalMatchesFound = job.TotalMatchesFound;
        tracked.ErrorMessage = job.ErrorMessage;
        tracked.CompletedAt = job.CompletedAt;
        tracked.Threshold = job.Threshold;

        await context.SaveChangesAsync();
    }

    public async Task<List<MatchJob>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.MatchJobs
            .AsNoTracking()
            .Include(j => j.DatasetA)
            .Include(j => j.DatasetB)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();
    }

    public async Task<MatchJob?> GetWithResultsAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.MatchJobs
            .AsNoTracking()
            .Include(j => j.DatasetA)
            .Include(j => j.DatasetB)
            .Include(j => j.Results).ThenInclude(r => r.RecordA)
            .Include(j => j.Results).ThenInclude(r => r.RecordB)
            .AsSplitQuery()
            .FirstOrDefaultAsync(j => j.Id == id);
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var job = await context.MatchJobs.FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
            return;

        context.MatchJobs.Remove(job);   // cascades to MatchResults
        await context.SaveChangesAsync();
    }
}
