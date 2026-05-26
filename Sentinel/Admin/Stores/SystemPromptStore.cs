using System.Text.Json;
using Sentinel.Admin.Models;
using StackExchange.Redis;

namespace Sentinel.Admin.Stores;

public class SystemPromptStore(IConnectionMultiplexer redis) : ISystemPromptStore
{
    private readonly IDatabase _db = redis.GetDatabase();
    private const string CurrentKey = "sentinel:prompt:current";
    private const string HistoryKey = "sentinel:prompt:history";

    public async Task<SystemPromptOverride?> GetOverrideAsync()
    {
        var json = await _db.StringGetAsync(CurrentKey);
        return json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<SystemPromptOverride>((string)json!);
    }

    public async Task SaveOverrideAsync(string promptText, string updatedBy)
    {
        var entry = new SystemPromptOverride
        {
            PromptText = promptText,
            UpdatedBy = updatedBy,
            UpdatedAt = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(entry);
        await _db.StringSetAsync(CurrentKey, json);
        await _db.ListLeftPushAsync(HistoryKey, json);
        await _db.ListTrimAsync(HistoryKey, 0, 9); // keep last 10
    }

    public async Task ClearOverrideAsync()
    {
        await _db.KeyDeleteAsync(CurrentKey);
    }

    public async Task<List<SystemPromptOverride>> GetHistoryAsync(int limit = 5)
    {
        var entries = await _db.ListRangeAsync(HistoryKey, 0, limit - 1);
        return entries
            .Select(e => JsonSerializer.Deserialize<SystemPromptOverride>((string)e!))
            .Where(e => e != null)
            .Select(e => e!)
            .ToList();
    }
}
