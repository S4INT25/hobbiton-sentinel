namespace Sentinel.Memory;

/// <summary>
/// Represents an ongoing fraud investigation case tracked across multiple hourly runs.
/// </summary>
public class FraudCase
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpper();
    public string Title { get; set; } = string.Empty;

    public string Category { get; set; } =
        string.Empty; // ghost_tx | unknown_ip | bulk_disbursement | unverified_merchant | known_recipient | admin_compromise | pattern

    public string Severity { get; set; } = "medium"; // low | medium | high | critical
    public string Status { get; set; } = "open"; // open | escalated | watching | resolved
    public int Confidence { get; set; } = 50; // 0-100 confidence that this is real fraud
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public int OccurrenceCount { get; set; } = 1;
    public List<string> AffectedEntities { get; set; } = []; // merchant IDs, IPs, wallet IDs, phone numbers
    public List<CaseEvidence> Evidence { get; set; } = [];
    public List<string> FollowUpQueries { get; set; } = []; // SQL queries to run next time
    public string Notes { get; set; } = string.Empty; // LLM analyst notes
    public string? Resolution { get; set; }

    /// <summary>The workflow that created this case. Null = legacy/global.</summary>
    public string? WorkflowId { get; set; }
}

public class CaseEvidence
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string RunId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string RawData { get; set; } = string.Empty; // truncated SQL result
}