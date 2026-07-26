using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public interface IProviderStore
{
    Task<List<ProviderConfig>> GetAllAsync();
    Task<List<ProviderConfig>> GetEnabledAsync();
    Task<ProviderConfig?> GetByIdAsync(int id);
    Task<ProviderConfig?> GetBySlugAsync(string slug);
    Task UpsertAsync(ProviderConfig provider);
    Task DeleteAsync(int id);
    Task SeedDefaultsAsync();
}
