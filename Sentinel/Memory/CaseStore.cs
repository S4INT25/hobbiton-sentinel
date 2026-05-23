using System.Text.Json;
using StackExchange.Redis;

namespace Sentinel.Memory;

/// <summary>
/// Persists fraud cases in Redis so the agent can track patterns across hourly runs.
/// </summary>
public class CaseStore(IConnectionMultiplexer redis, ILogger<CaseStore> logger) : ICaseStore
{
    private readonly IDatabase _db = redis.GetDatabase();
    private const string CaseSetKey = "fraud:cases";
    private const string CaseKeyPrefix = "fraud:case:";
    private static readonly TimeSpan CaseTtl = TimeSpan.FromDays(30); // auto-expire resolved cases

    public async Task<List<FraudCase>> GetOpenCasesAsync()
    {
        var caseIds = await _db.SetMembersAsync(CaseSetKey);
        var cases = new List<FraudCase>();

        foreach (var id in caseIds)
        {
            var json = await _db.StringGetAsync($"{CaseKeyPrefix}{id}");
            if (json.IsNullOrEmpty) continue;

            var c = JsonSerializer.Deserialize<FraudCase>((string)json!);
            if (c != null && c.Status != "resolved")
                cases.Add(c);
        }

        return cases.OrderByDescending(c => c.Severity switch
        {
            "critical" => 4, "high" => 3, "medium" => 2, _ => 1
        }).ToList();
    }

    public async Task<FraudCase> SaveCaseAsync(FraudCase fraudCase)
    {
        fraudCase.LastSeen = DateTime.UtcNow;
        var key = $"{CaseKeyPrefix}{fraudCase.Id}";
        var json = JsonSerializer.Serialize(fraudCase, new JsonSerializerOptions { WriteIndented = false });

        await _db.StringSetAsync(key, json, CaseTtl);
        await _db.SetAddAsync(CaseSetKey, fraudCase.Id);

        logger.LogInformation("Case {Id} saved: {Title} [{Status}]",
            fraudCase.Id, fraudCase.Title, fraudCase.Status);

        return fraudCase;
    }

    public async Task<FraudCase?> GetCaseAsync(string id)
    {
        var json = await _db.StringGetAsync($"{CaseKeyPrefix}{id}");
        return json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<FraudCase>((string)json!);
    }

    public async Task ResolveCaseAsync(string id, string resolution)
    {
        var c = await GetCaseAsync(id);
        if (c == null) return;

        c.Status = "resolved";
        c.Resolution = resolution;
        await SaveCaseAsync(c);

        // Remove from active set
        await _db.SetRemoveAsync(CaseSetKey, id);
        logger.LogInformation("Case {Id} resolved: {Resolution}", id, resolution);
    }

    /// <summary>Returns a compact summary of all open cases for the LLM system prompt.</summary>
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
