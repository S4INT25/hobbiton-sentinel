using System.Collections.Concurrent;

namespace Sentinel.Admin;

public class ActiveRunTracker
{
    private readonly ConcurrentDictionary<string, ActiveRunState> _runs = new();

    public void MarkQueued(string runId, string triggeredBy, DateTime startedAtUtc)
    {
        _runs.AddOrUpdate(runId,
            _ => new ActiveRunState(runId, "queued", triggeredBy, startedAtUtc, DateTime.UtcNow),
            (_, current) => current with { Status = "queued", TriggeredBy = triggeredBy, StartedAtUtc = startedAtUtc, UpdatedAtUtc = DateTime.UtcNow });
    }

    public void MarkRunning(string runId, string triggeredBy, DateTime startedAtUtc)
    {
        _runs.AddOrUpdate(runId,
            _ => new ActiveRunState(runId, "running", triggeredBy, startedAtUtc, DateTime.UtcNow),
            (_, current) => current with { Status = "running", TriggeredBy = triggeredBy, StartedAtUtc = startedAtUtc, UpdatedAtUtc = DateTime.UtcNow });
    }

    public void MarkFailed(string runId)
    {
        if (_runs.TryGetValue(runId, out var current))
            _runs[runId] = current with { Status = "failed", UpdatedAtUtc = DateTime.UtcNow };
    }

    public void MarkCompleted(string runId)
    {
        _runs.TryRemove(runId, out _);
    }

    public bool TryGet(string runId, out ActiveRunState state) => _runs.TryGetValue(runId, out state!);

    public ActiveRunState? GetLatestTrackedRun()
    {
        return _runs.Values
            .OrderByDescending(r => r.UpdatedAtUtc)
            .FirstOrDefault();
    }
}

public sealed record ActiveRunState(
    string RunId,
    string Status,
    string TriggeredBy,
    DateTime StartedAtUtc,
    DateTime UpdatedAtUtc);
