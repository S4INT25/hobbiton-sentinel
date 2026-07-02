namespace Sentinel.Admin.Models;

public class FeedbackRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Scope { get; set; } = "global"; // global, merchant, user
    public string? ScopeId { get; set; }
    public string RuleType { get; set; } = ""; // ip, cidr, asn, pattern_id, keyword, recipient
    public string MatchValue { get; set; } = "";
    public string Action { get; set; } = "suppress"; // suppress, downgrade, info_only
    public string Reason { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public int HitCount { get; set; }
    public DateTime? LastHitAt { get; set; }
    public string? SourceCaseId { get; set; }

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
    public bool IsActive => !IsExpired;
}