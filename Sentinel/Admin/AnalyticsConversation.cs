namespace Sentinel.Admin;

public class AnalyticsConversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Title { get; set; } = "New Conversation";
    public string Database { get; set; } = string.Empty;
    public List<ChatEntry> Messages { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string UserId { get; set; } = "default";
}

public class ChatEntry
{
    public string Role { get; set; } = "user"; // user | assistant
    public string Content { get; set; } = string.Empty;
    public AnalyticsResponse? Response { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
