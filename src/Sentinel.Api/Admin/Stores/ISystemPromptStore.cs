using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public interface ISystemPromptStore
{
    Task<SystemPromptOverride?> GetOverrideAsync();
    Task SaveOverrideAsync(string promptText, string updatedBy);
    Task ClearOverrideAsync();
    Task<List<SystemPromptOverride>> GetHistoryAsync(int limit = 5);
}