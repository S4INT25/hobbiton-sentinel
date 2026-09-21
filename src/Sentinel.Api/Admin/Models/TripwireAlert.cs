namespace Sentinel.Admin.Models;

/// <summary>
/// One tripwire firing. Exists for two reasons: suppressing repeat alerts about the same entity
/// (see <see cref="FiredAt"/> plus the rule's cooldown), and giving the detector an auditable
/// history — "why did nobody get paged about this merchant" is answerable only if near-misses
/// and suppressions are on record.
/// </summary>
public class TripwireAlert
{
    public long Id { get; set; }

    /// <summary>Tripwire name, matching <see cref="Sentinel.Jobs.Tripwire.Name"/>.</summary>
    public string RuleName { get; set; } = "";

    /// <summary>The entity cooled down on — merchant id, account number. From the query's entity_key column.</summary>
    public string EntityKey { get; set; } = "";

    /// <summary>The matching row as JSON, so the alert stays readable after the window has passed.</summary>
    public string Detail { get; set; } = "";

    /// <summary>Fraud agent run enqueued for this hit, or null if enqueuing failed.</summary>
    public string? RunId { get; set; }

    public DateTimeOffset FiredAt { get; set; } = DateTimeOffset.UtcNow;
}
