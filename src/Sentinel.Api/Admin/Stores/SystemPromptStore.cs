using Sentinel.Admin.Models;
using ZiggyCreatures.Caching.Fusion;

namespace Sentinel.Admin.Stores;

public class SystemPromptStore(IFusionCache cache) : ISystemPromptStore
{
    private const string CurrentKey = "sentinel:prompt:current";
    private const string HistoryKey = "sentinel:prompt:history";

    public async Task<SystemPromptOverride?> GetOverrideAsync() =>
        await cache.GetOrDefaultAsync<SystemPromptOverride>(CurrentKey);

    public async Task SaveOverrideAsync(string promptText, string updatedBy)
    {
        var entry = new SystemPromptOverride
        {
            PromptText = promptText,
            UpdatedBy = updatedBy,
            UpdatedAt = DateTime.UtcNow
        };

        await cache.SetAsync(CurrentKey, entry, o => o.SetDuration(TimeSpan.MaxValue));

        var history = await cache.GetOrDefaultAsync<List<SystemPromptOverride>>(HistoryKey) ?? [];
        history.Insert(0, entry);
        if (history.Count > 10) history = history.Take(10).ToList();
        await cache.SetAsync(HistoryKey, history, o => o.SetDuration(TimeSpan.MaxValue));
    }

    public async Task ClearOverrideAsync() =>
        await cache.RemoveAsync(CurrentKey);

    public async Task<List<SystemPromptOverride>> GetHistoryAsync(int limit = 5) =>
        (await cache.GetOrDefaultAsync<List<SystemPromptOverride>>(HistoryKey) ?? []).Take(limit).ToList();
}