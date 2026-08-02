using ZiggyCreatures.Caching.Fusion;

namespace Sentinel.Admin;

public class ActiveRunTracker(IFusionCache cache, ILogger<ActiveRunTracker> logger) : IActiveRunTracker
{
    private const string IndexKey = "sentinel:active-runs:index";
    private static readonly TimeSpan RunTtl = TimeSpan.FromHours(24);

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

    public async Task<IReadOnlyList<ActiveRunState>> GetActiveRunsAsync()
    {
        var index = await cache.GetOrDefaultAsync<List<string>>(IndexKey) ?? [];
        var active = new List<ActiveRunState>();
        var stale = new List<string>();

        foreach (var runId in index)
        {
            var state = await cache.GetOrDefaultAsync<ActiveRunState>(Key(runId));
            if (state is null)
            {
                stale.Add(runId);
                continue;
            }

            // Terminal entries linger for their TTL so GetAsync can still describe how a run
            // ended; they are not "active" and must not be reported as such.
            if (ActiveRunStatuses.IsInFlight(state.Status)) active.Add(state);
        }

        if (stale.Count > 0)
        {
            index.RemoveAll(stale.Contains);
            await cache.SetAsync(IndexKey, index, o => o.SetDuration(TimeSpan.MaxValue));
        }

        return active;
    }

    private static string Key(string runId) => $"sentinel:active-run:{runId}";

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

public static class ActiveRunStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";

    /// <summary>
    /// Whether a tracked run is still working. Failed and stopped entries stay in the tracker for
    /// their TTL so the detail page can report how a run ended, so presence in the tracker is not
    /// the same as being active — without this check a workflow keeps advertising "running" for a
    /// day after it died.
    /// </summary>
    public static bool IsInFlight(string? status) =>
        status is Queued or Running;
}