using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class PostgresAuditLogStore(IDbContextFactory<SentinelDbContext> dbFactory) : IAuditLogStore
{
    public async Task LogAsync(AuditLog entry)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.AuditLogs.Add(entry);
        await db.SaveChangesAsync();
    }

    public async Task<List<AuditLog>> GetRecentAsync(int limit = 100, int offset = 0,
        string? userId = null, string? action = null, string? resourceType = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(userId))       query = query.Where(a => a.UserId == userId);
        if (!string.IsNullOrEmpty(action))        query = query.Where(a => a.Action == action);
        if (!string.IsNullOrEmpty(resourceType)) query = query.Where(a => a.ResourceType == resourceType);

        return await query
            .OrderByDescending(a => a.Timestamp)
            .Skip(offset).Take(limit)
            .ToListAsync();
    }
}
