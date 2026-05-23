using System.Collections.Concurrent;

namespace Sentinel.Memory;

/// <summary>
/// In-memory ICaseStore for local development — no Redis required.
/// Data is lost when the process restarts.
/// </summary>
public class InMemoryCaseStore(ILogger<InMemoryCaseStore> logger) : ICaseStore
{
    private readonly ConcurrentDictionary<string, FraudCase> _cases = new();

    public Task<List<FraudCase>> GetOpenCasesAsync()
    {
        var open = _cases.Values
            .Where(c => c.Status != "resolved")
            .OrderByDescending(c => c.Severity switch
            {
                "critical" => 4, "high" => 3, "medium" => 2, _ => 1
            })
            .ToList();

        return Task.FromResult(open);
    }

    public Task<FraudCase> SaveCaseAsync(FraudCase fraudCase)
    {
        fraudCase.LastSeen = DateTime.UtcNow;
        _cases[fraudCase.Id] = fraudCase;
        logger.LogInformation("Case {Id} saved: {Title} [{Status}]",
            fraudCase.Id, fraudCase.Title, fraudCase.Status);
        return Task.FromResult(fraudCase);
    }

    public Task<FraudCase?> GetCaseAsync(string id)
    {
        _cases.TryGetValue(id, out var c);
        return Task.FromResult(c);
    }

    public async Task ResolveCaseAsync(string id, string resolution)
    {
        var c = await GetCaseAsync(id);
        if (c == null) return;
        c.Status = "resolved";
        c.Resolution = resolution;
        await SaveCaseAsync(c);
        logger.LogInformation("Case {Id} resolved: {Resolution}", id, resolution);
    }

    public async Task<string> GetOpenCasesSummaryAsync()
    {
        var cases = await GetOpenCasesAsync();
        if (cases.Count == 0) return "No open cases.";

        var sb = new System.Text.StringBuilder();
        foreach (var c in cases)
        {
            sb.AppendLine($"- [{c.Severity.ToUpper()}] Case {c.Id}: {c.Title}");
            sb.AppendLine($"  Category: {c.Category} | Status: {c.Status} | Seen {c.OccurrenceCount}x | First: {c.FirstSeen:yyyy-MM-dd HH:mm} UTC | Last: {c.LastSeen:yyyy-MM-dd HH:mm} UTC");
            sb.AppendLine($"  Entities: {string.Join(", ", c.AffectedEntities.Take(5))}");
            if (c.FollowUpQueries.Count > 0)
                sb.AppendLine($"  Follow-up queries suggested: {c.FollowUpQueries.Count}");
            sb.AppendLine($"  Notes: {c.Notes[..Math.Min(200, c.Notes.Length)]}");
        }
        return sb.ToString();
    }
}
