using System.Text.Json;
using StackExchange.Redis;

namespace Sentinel.Admin;

/// <summary>Redis-backed active run tracker. Survives server restarts and horizontal scaling.</summary>
public class RedisActiveRunTracker(IConnectionMultiplexer redis, ILogger<RedisActiveRunTracker> logger)
    : IActiveRunTracker
{
    private readonly IDatabase _db = redis.GetDatabase();
    private static readonly TimeSpan RunTtl = TimeSpan.FromHours(24);

    private static string Key(string runId) => $"sentinel:active-run:{runId}";
    private const string IndexKey = "sentinel:active-runs:index";

    public async Task MarkQueuedAsync(string runId, string triggeredBy, DateTime startedAtUtc)
        => await UpsertAsync(runId, "queued", triggeredBy, startedAtUtc);

    public async Task MarkRunningAsync(string runId, string triggeredBy, DateTime startedAtUtc)
        => await UpsertAsync(runId, "running", triggeredBy, startedAtUtc);

    public async Task MarkFailedAsync(string runId)
    {
        var state = await GetAsync(runId);
        if (state is null) return;
        await UpsertAsync(runId, "failed", state.TriggeredBy, state.StartedAtUtc);
    }

    public async Task MarkStoppedAsync(string runId)
    {
        var state = await GetAsync(runId);
        if (state is null) return;
        await UpsertAsync(runId, "stopped", state.TriggeredBy, state.StartedAtUtc);
    }

    public async Task MarkCompletedAsync(string runId)
    {
        await _db.KeyDeleteAsync(Key(runId));
        await _db.SortedSetRemoveAsync(IndexKey, runId);
        logger.LogDebug("Run {RunId} removed from active tracker", runId);
    }

    public async Task<ActiveRunState?> GetAsync(string runId)
    {
        var json = await _db.StringGetAsync(Key(runId));
        if (json.IsNullOrEmpty) return null;
        try { return JsonSerializer.Deserialize<ActiveRunState>((string)json!); }
        catch { return null; }
    }

    public async Task<ActiveRunState?> GetLatestTrackedRunAsync()
    {
        // Sorted set scores = UpdatedAtUtc.Ticks, so highest score = most recent
        var members = await _db.SortedSetRangeByRankAsync(IndexKey, 0, 19, Order.Descending);
        foreach (var member in members)
        {
            var state = await GetAsync(member!);
            if (state != null) return state;
            // Key expired or missing — clean up index
            await _db.SortedSetRemoveAsync(IndexKey, member);
        }
        return null;
    }

    private async Task UpsertAsync(string runId, string status, string triggeredBy, DateTime startedAtUtc)
    {
        var state = new ActiveRunState(runId, status, triggeredBy, startedAtUtc, DateTime.UtcNow);
        var json = JsonSerializer.Serialize(state);
        await _db.StringSetAsync(Key(runId), json, RunTtl);
        await _db.SortedSetAddAsync(IndexKey, runId, state.UpdatedAtUtc.Ticks);
        logger.LogDebug("Run {RunId} marked {Status}", runId, status);
    }
}
