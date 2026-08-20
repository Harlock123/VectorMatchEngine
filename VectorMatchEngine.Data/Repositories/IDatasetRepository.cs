using VectorMatchEngine.Data.Entities;

namespace VectorMatchEngine.Data.Repositories;

public interface IDatasetRepository
{
    Task<Dataset> CreateAsync(Dataset dataset);
    Task<List<Dataset>> GetAllAsync();
    Task<Dataset?> GetWithRecordsAsync(int id);
    Task<Dataset?> GetAsync(int id);
    Task DeleteAsync(int id);
}
