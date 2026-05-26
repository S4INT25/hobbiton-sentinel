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
    public AnalyticsResponse? Result { get; set; }
    public string? Error { get; set; }
    /// <summary>Live streaming events written by the agent as it reasons and retries.</summary>
    public List<AnalyticsStreamEvent> StreamEvents { get; set; } = [];
}

public class AnalyticsStreamEvent
{
    /// <summary>thinking | sql | executing | error | fixing | done</summary>
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Sql { get; set; }
    public int Attempt { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
