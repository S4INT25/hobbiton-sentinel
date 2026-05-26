using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class RunLogStore(SentinelClickHouseContext db) : IRunLogStore
{
    public async Task LogToolCallAsync(RunLog entry)
    {
        db.RunLogs.Add(entry);
        await db.SaveChangesAsync();
    }

    public async Task SaveSummaryAsync(RunSummary summary)
    {
        db.RunSummaries.Add(summary);
        await db.SaveChangesAsync();
    }

    public async Task<List<RunSummary>> GetRecentRunsAsync(int limit = 50, int offset = 0)
    {
        return await db.RunSummaries
            .OrderByDescending(r => r.StartedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<RunSummary?> GetRunSummaryAsync(string runId)
    {
        return await db.RunSummaries
            .FirstOrDefaultAsync(r => r.RunId == runId);
    }

    public async Task<List<RunLog>> GetRunLogsAsync(string runId)
    {
        return await db.RunLogs
            .Where(r => r.RunId == runId)
            .OrderBy(r => r.Iteration)
            .ThenBy(r => r.StartedAt)
            .ToListAsync();
    }
}
