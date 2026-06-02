using ClickHouse.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class RunLogStore(IDbContextFactory<SentinelClickHouseContext> dbFactory) : IRunLogStore
{
    public async Task LogToolCallAsync(RunLog entry)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.BulkInsertAsync([entry]);
    }

    public async Task SaveSummaryAsync(RunSummary summary)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.BulkInsertAsync([summary]);
    }

    public async Task<List<RunSummary>> GetRecentRunsAsync(int limit = 50, int offset = 0)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.RunSummaries
            .OrderByDescending(r => r.StartedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<RunSummary?> GetRunSummaryAsync(string runId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.RunSummaries
            .FirstOrDefaultAsync(r => r.RunId == runId);
    }

    public async Task<List<RunLog>> GetRunLogsAsync(string runId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.RunLogs
            .Where(r => r.RunId == runId)
            .OrderBy(r => r.Iteration)
            .ThenBy(r => r.StartedAt)
            .ToListAsync();
    }
}
