using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public interface ILlmModelStore
{
    Task<List<LlmModel>> GetAllAsync();
    Task<List<LlmModel>> GetEnabledAsync();
    Task<LlmModel?> GetByIdAsync(int id);
    Task UpsertAsync(LlmModel model);
    Task DeleteAsync(int id);
    Task SeedDefaultsAsync();
}
