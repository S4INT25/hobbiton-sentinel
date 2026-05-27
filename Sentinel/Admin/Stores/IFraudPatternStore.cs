using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public interface IFraudPatternStore
{
    Task<List<FraudPatternEntity>> GetAllAsync();
    Task<List<FraudPatternEntity>> GetEnabledAsync();
    Task<FraudPatternEntity?> GetByIdAsync(int id);
    Task UpsertAsync(FraudPatternEntity pattern);
    Task DeleteAsync(int id);
    Task EnsureTableAsync();
    Task SeedDefaultsAsync();
}
