using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public interface IEvidenceSourceStore
{
    Task<List<EvidenceSource>> GetAllAsync();
    Task<List<EvidenceSource>> GetEnabledAsync();
    Task<EvidenceSource?> GetByIdAsync(int id);
    Task UpsertAsync(EvidenceSource source);
    Task DeleteAsync(int id);
    Task EnsureTableAsync();
    Task SeedDefaultsAsync();
}
