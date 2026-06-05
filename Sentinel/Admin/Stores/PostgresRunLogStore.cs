using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class PostgresRunLogStore(IDbContextFactory<SentinelDbContext> dbFactory) : IRunLogStore
{
    public async Task LogToolCallAsync(RunLog entry)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.RunLogs.Add(entry);
        await db.SaveChangesAsync();
    }

    public async Task SaveSummaryAsync(RunSummary summary)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.RunSummaries.FirstOrDefaultAsync(r => r.RunId == summary.RunId);
        if (existing is null)
        {
            db.RunSummaries.Add(summary);
        }
        else
        {
            existing.FinishedAt = summary.FinishedAt;
            existing.Iterations = summary.Iterations;
            existing.InputTokens = summary.InputTokens;
            existing.OutputTokens = summary.OutputTokens;
            existing.CasesCreated = summary.CasesCreated;
            existing.CasesResolved = summary.CasesResolved;
            existing.AlertsSent = summary.AlertsSent;
            existing.Status = summary.Status;
            if (summary.EmailSubject != null) existing.EmailSubject = summary.EmailSubject;
            if (summary.EmailBody    != null) existing.EmailBody    = summary.EmailBody;
        }
        await db.SaveChangesAsync();
    }

    public async Task<List<RunSummary>> GetRecentRunsAsync(int limit = 50, int offset = 0)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.RunSummaries
            .AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .Skip(offset).Take(limit)
            .ToListAsync();
    }

    public async Task<RunSummary?> GetRunSummaryAsync(string runId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.RunSummaries.AsNoTracking().FirstOrDefaultAsync(r => r.RunId == runId);
    }

    public async Task<List<RunLog>> GetRunLogsAsync(string runId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.RunLogs
            .AsNoTracking()
            .Where(r => r.RunId == runId)
            .OrderBy(r => r.Iteration).ThenBy(r => r.StartedAt)
            .ToListAsync();
    }
}
