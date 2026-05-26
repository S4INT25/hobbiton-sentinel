using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sentinel.Admin.Models;

[Table("audit_logs")]
public class AuditLog
{
    [Column("id")] public Guid Id { get; set; } = Guid.NewGuid();
    [Column("user_id")] public string UserId { get; set; } = "";
    [Column("username")] public string Username { get; set; } = "";
    [Column("action")] public string Action { get; set; } = "";
    [Column("resource_type")] public string ResourceType { get; set; } = "";
    [Column("resource_id")] public string ResourceId { get; set; } = "";
    [Column("details")] public string Details { get; set; } = "";
    [Column("ip_address")] public string IpAddress { get; set; } = "";
    [Column("timestamp")] public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
