using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class InMemoryRunLogStore : IRunLogStore
{
    private readonly List<RunLog> _logs = [];
    private readonly List<RunSummary> _summaries = [];

    public Task LogToolCallAsync(RunLog entry) { _logs.Add(entry); return Task.CompletedTask; }
    public Task SaveSummaryAsync(RunSummary summary) { _summaries.Add(summary); return Task.CompletedTask; }

    public Task<List<RunSummary>> GetRecentRunsAsync(int limit = 50, int offset = 0) =>
        Task.FromResult(_summaries.OrderByDescending(s => s.StartedAt).Skip(offset).Take(limit).ToList());

    public Task<RunSummary?> GetRunSummaryAsync(string runId) =>
        Task.FromResult(_summaries.FirstOrDefault(s => s.RunId == runId));

    public Task<List<RunLog>> GetRunLogsAsync(string runId) =>
        Task.FromResult(_logs.Where(l => l.RunId == runId).OrderBy(l => l.Iteration).ToList());
}
