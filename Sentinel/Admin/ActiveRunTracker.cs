using ZiggyCreatures.Caching.Fusion;

namespace Sentinel.Admin;

public class ActiveRunTracker(IFusionCache cache, ILogger<ActiveRunTracker> logger) : IActiveRunTracker
{
    private static readonly TimeSpan RunTtl = TimeSpan.FromHours(24);
    private const string IndexKey = "sentinel:active-runs:index";

    private static string Key(string runId) => $"sentinel:active-run:{runId}";

    public Task MarkQueuedAsync(string runId, string triggeredBy, DateTime startedAtUtc) =>
        UpsertAsync(runId, "queued", triggeredBy, startedAtUtc);

    public Task MarkRunningAsync(string runId, string triggeredBy, DateTime startedAtUtc) =>
        UpsertAsync(runId, "running", triggeredBy, startedAtUtc);

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
        await cache.RemoveAsync(Key(runId));
        var index = await cache.GetOrDefaultAsync<List<string>>(IndexKey) ?? [];
        index.Remove(runId);
        await cache.SetAsync(IndexKey, index, o => o.SetDuration(TimeSpan.MaxValue));
        logger.LogDebug("Run {RunId} removed from active tracker", runId);
    }

    public async Task<ActiveRunState?> GetAsync(string runId) =>
        await cache.GetOrDefaultAsync<ActiveRunState>(Key(runId));

    public async Task<ActiveRunState?> GetLatestTrackedRunAsync()
    {
        var index = await cache.GetOrDefaultAsync<List<string>>(IndexKey) ?? [];
        var stale = new List<string>();

        foreach (var runId in index)
        {
            var state = await cache.GetOrDefaultAsync<ActiveRunState>(Key(runId));
            if (state != null) return state;
            stale.Add(runId); // run TTL expired, clean up index
        }

        if (stale.Count > 0)
        {
            index.RemoveAll(stale.Contains);
            await cache.SetAsync(IndexKey, index, o => o.SetDuration(TimeSpan.MaxValue));
        }

        return null;
    }

    private async Task UpsertAsync(string runId, string status, string triggeredBy, DateTime startedAtUtc)
    {
        var state = new ActiveRunState(runId, status, triggeredBy, startedAtUtc, DateTime.UtcNow);
        await cache.SetAsync(Key(runId), state, o => o.SetDuration(RunTtl));

        var index = await cache.GetOrDefaultAsync<List<string>>(IndexKey) ?? [];
        index.Remove(runId);
        index.Insert(0, runId); // most recent first
        if (index.Count > 20) index = index.Take(20).ToList();
        await cache.SetAsync(IndexKey, index, o => o.SetDuration(TimeSpan.MaxValue));

        logger.LogDebug("Run {RunId} marked {Status}", runId, status);
    }
}

public sealed record ActiveRunState(
    string RunId,
    string Status,
    string TriggeredBy,
    DateTime StartedAtUtc,
    DateTime UpdatedAtUtc);
