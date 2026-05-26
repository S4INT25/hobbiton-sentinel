using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class InMemoryAuditLogStore : IAuditLogStore
{
    private readonly List<AuditLog> _logs = [];

    public Task LogAsync(AuditLog entry) { _logs.Add(entry); return Task.CompletedTask; }

    public Task<List<AuditLog>> GetRecentAsync(int limit = 100, int offset = 0,
        string? userId = null, string? action = null, string? resourceType = null)
    {
        var query = _logs.AsEnumerable();
        if (!string.IsNullOrEmpty(userId)) query = query.Where(a => a.UserId == userId);
        if (!string.IsNullOrEmpty(action)) query = query.Where(a => a.Action == action);
        if (!string.IsNullOrEmpty(resourceType)) query = query.Where(a => a.ResourceType == resourceType);
        return Task.FromResult(query.OrderByDescending(a => a.Timestamp).Skip(offset).Take(limit).ToList());
    }
}
