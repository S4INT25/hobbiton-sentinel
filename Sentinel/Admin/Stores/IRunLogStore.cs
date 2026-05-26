using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public interface IRunLogStore
{
    Task LogToolCallAsync(RunLog entry);
    Task SaveSummaryAsync(RunSummary summary);
    Task<List<RunSummary>> GetRecentRunsAsync(int limit = 50, int offset = 0);
    Task<RunSummary?> GetRunSummaryAsync(string runId);
    Task<List<RunLog>> GetRunLogsAsync(string runId);
}
