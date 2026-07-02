using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public interface IAuditLogStore
{
    Task LogAsync(AuditLog entry);

    Task<List<AuditLog>> GetRecentAsync(int limit = 100, int offset = 0,
        string? userId = null, string? action = null, string? resourceType = null);
}