using Sentinel.Agent;

namespace Sentinel.Admin.Models;

public class PresentationSlide
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Title { get; set; } = "Untitled Slide";
    public string? UserPrompt { get; set; }
    public string? Database { get; set; }

    // Snapshot from chat assistant response
    public string? Explanation { get; set; }
    public string? Summary { get; set; }
    public string? RiskLevel { get; set; }
    public List<string> Findings { get; set; } = [];
    public List<string> RecommendedActions { get; set; } = [];
    public string ChartType { get; set; } = "none";
    public List<string> Columns { get; set; } = [];
    public List<Dictionary<string, string>> Rows { get; set; } = [];
    public List<QueryResult> Results { get; set; } = [];

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

public class PresentationDeck
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Title { get; set; } = "Presentation";
    public string UserId { get; set; } = "default";
    public List<PresentationSlide> Slides { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
