namespace Sentinel.Admin;

public class AnalyticsQueryJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..16];
    public string ConversationId { get; set; } = string.Empty;
    public string UserId { get; set; } = "default";
    public string Prompt { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string Status { get; set; } = "pending"; // pending | running | completed | failed
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
}
