using VectorMatchEngine.Data.Entities;

namespace VectorMatchEngine.Data.Repositories;

public interface IMatchJobRepository
{
    Task<MatchJob> CreateAsync(MatchJob job);
    Task UpdateAsync(MatchJob job);
    Task<List<MatchJob>> GetAllAsync();
    Task<MatchJob?> GetWithResultsAsync(int id);
    Task DeleteAsync(int id);
}
