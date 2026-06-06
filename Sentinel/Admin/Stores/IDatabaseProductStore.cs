using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public interface IDatabaseProductStore
{
    Task<List<DatabaseProduct>> GetAllAsync();
    Task<List<DatabaseProduct>> GetEnabledAsync();
    Task<DatabaseProduct?> GetByIdAsync(int id);
    Task UpsertAsync(DatabaseProduct product);
    Task DeleteAsync(int id);
    Task SeedDefaultsAsync();
}