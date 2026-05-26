using System.Collections.Concurrent;

namespace Sentinel.Admin;

/// <summary>In-memory implementation used in development / when Redis is unavailable.</summary>
public class InMemoryActiveRunTracker : IActiveRunTracker
{
    private readonly ConcurrentDictionary<string, ActiveRunState> _runs = new();

    public Task MarkQueuedAsync(string runId, string triggeredBy, DateTime startedAtUtc)
    {
        _runs.AddOrUpdate(runId,
            _ => new ActiveRunState(runId, "queued", triggeredBy, startedAtUtc, DateTime.UtcNow),
            (_, cur) => cur with { Status = "queued", TriggeredBy = triggeredBy, StartedAtUtc = startedAtUtc, UpdatedAtUtc = DateTime.UtcNow });
        return Task.CompletedTask;
    }

    public Task MarkRunningAsync(string runId, string triggeredBy, DateTime startedAtUtc)
    {
        _runs.AddOrUpdate(runId,
            _ => new ActiveRunState(runId, "running", triggeredBy, startedAtUtc, DateTime.UtcNow),
            (_, cur) => cur with { Status = "running", TriggeredBy = triggeredBy, StartedAtUtc = startedAtUtc, UpdatedAtUtc = DateTime.UtcNow });
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(string runId)
    {
        if (_runs.TryGetValue(runId, out var cur))
            _runs[runId] = cur with { Status = "failed", UpdatedAtUtc = DateTime.UtcNow };
        return Task.CompletedTask;
    }

    public Task MarkCompletedAsync(string runId)
    {
        _runs.TryRemove(runId, out _);
        return Task.CompletedTask;
    }

    public Task<ActiveRunState?> GetAsync(string runId) =>
        Task.FromResult(_runs.TryGetValue(runId, out var state) ? state : (ActiveRunState?)null);

    public Task<ActiveRunState?> GetLatestTrackedRunAsync() =>
        Task.FromResult(_runs.Values.OrderByDescending(r => r.UpdatedAtUtc).FirstOrDefault());
}

public sealed record ActiveRunState(
    string RunId,
    string Status,
    string TriggeredBy,
    DateTime StartedAtUtc,
    DateTime UpdatedAtUtc);
