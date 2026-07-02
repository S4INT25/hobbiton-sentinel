namespace Sentinel.Admin;

public interface IActiveRunTracker
{
    Task MarkQueuedAsync(string runId, string triggeredBy, DateTime startedAtUtc);
    Task MarkRunningAsync(string runId, string triggeredBy, DateTime startedAtUtc);
    Task MarkFailedAsync(string runId);
    Task MarkStoppedAsync(string runId);
    Task MarkCompletedAsync(string runId);
    Task<ActiveRunState?> GetAsync(string runId);
    Task<ActiveRunState?> GetLatestTrackedRunAsync();
    Task<IReadOnlyList<ActiveRunState>> GetActiveRunsAsync();
}