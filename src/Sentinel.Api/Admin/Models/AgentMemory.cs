namespace Sentinel.Admin.Models;

/// <summary>
/// A business definition or calculation rule the analytics agent should know about.
/// Admins define how metrics like "Revenue" or "Active Merchant" are calculated,
/// and the agent uses these definitions when answering questions.
/// </summary>
public class AgentMemory
{
    public int Id { get; set; }
    public string Term { get; set; } = "";
    public string Definition { get; set; } = "";

    /// <summary>Null = applies to all databases; otherwise scoped to a specific database.</summary>
    public string? Database { get; set; }

    /// <summary>Null = applies to all workflows; otherwise scoped to a specific workflow.</summary>
    public string? WorkflowId { get; set; }

    public bool Enabled { get; set; } = true;
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}