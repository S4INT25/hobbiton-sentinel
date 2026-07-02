namespace Sentinel.Admin.Models;

public class FraudPatternEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "TransactionAnomaly";
    public bool Enabled { get; set; } = true;

    /// <summary>Optional workflow scope. Null/empty = global (available to all workflows).</summary>
    public string? WorkflowId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = "system";
}