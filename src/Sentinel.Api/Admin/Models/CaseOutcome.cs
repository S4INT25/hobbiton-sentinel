using System.ComponentModel.DataAnnotations.Schema;

namespace Sentinel.Admin.Models;

/// <summary>
/// Persistent record of how a fraud case was ultimately resolved.
/// Stored in PostgreSQL (not cache) so the agent can learn from historical
/// decisions — which patterns were real fraud vs false positives.
/// </summary>
[Table("case_outcomes")]
public class CaseOutcome
{
    [Column("id")] public int Id { get; set; }

    /// <summary>Original case ID from the FraudCase (8-char hex).</summary>
    [Column("case_id")]
    public string CaseId { get; set; } = "";

    /// <summary>Short title of the case.</summary>
    [Column("title")]
    public string Title { get; set; } = "";

    /// <summary>Fraud category: ghost_tx, unknown_ip, bulk_disbursement, etc.</summary>
    [Column("category")]
    public string Category { get; set; } = "";

    /// <summary>Which fraud pattern ID triggered this (if any).</summary>
    [Column("pattern_id")]
    public int? PatternId { get; set; }

    /// <summary>Final outcome: confirmed_fraud | false_positive | inconclusive | auto_resolved</summary>
    [Column("outcome")]
    public string Outcome { get; set; } = "inconclusive";

    /// <summary>Original severity when the case was created.</summary>
    [Column("original_severity")]
    public string OriginalSeverity { get; set; } = "medium";

    /// <summary>Confidence score (0-100) assigned by the agent when creating the case.</summary>
    [Column("confidence")]
    public int Confidence { get; set; }

    /// <summary>Affected merchant IDs, IPs, wallets, phones (comma-separated).</summary>
    [Column("affected_entities")]
    public string AffectedEntities { get; set; } = "";

    /// <summary>The target database this case was detected in.</summary>
    [Column("database")]
    public string Database { get; set; } = "";

    /// <summary>Workflow that created this case (null = global/legacy).</summary>
    [Column("workflow_id")]
    public string? WorkflowId { get; set; }

    /// <summary>Human or agent explanation of the resolution.</summary>
    [Column("resolution")]
    public string? Resolution { get; set; }

    /// <summary>Who resolved it: agent | analyst | auto_stale</summary>
    [Column("resolved_by")]
    public string ResolvedBy { get; set; } = "agent";

    /// <summary>How many hourly runs observed this pattern before resolution.</summary>
    [Column("occurrence_count")]
    public int OccurrenceCount { get; set; } = 1;

    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("resolved_at")] public DateTime ResolvedAt { get; set; } = DateTime.UtcNow;
}