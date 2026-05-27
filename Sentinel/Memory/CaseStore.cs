using ZiggyCreatures.Caching.Fusion;

namespace Sentinel.Memory;

/// <summary>
/// Persists fraud cases via FusionCache (L1 memory in dev, L1+L2 Redis in prod).
/// </summary>
public class CaseStore(IFusionCache cache, ILogger<CaseStore> logger) : ICaseStore
{
    private const string StoreKey = "fraud:cases";
    private static readonly TimeSpan CaseTtl = TimeSpan.FromDays(30);

    private async Task<Dictionary<string, FraudCase>> LoadAsync() =>
        await cache.GetOrDefaultAsync<Dictionary<string, FraudCase>>(StoreKey) ?? [];

    private Task PersistAsync(Dictionary<string, FraudCase> cases) =>
        cache.SetAsync(StoreKey, cases, o => o.SetDuration(CaseTtl)).AsTask();

    public async Task<List<FraudCase>> GetOpenCasesAsync()
    {
        var all = await LoadAsync();
        return all.Values
            .Where(c => c.Status != "resolved")
            .OrderByDescending(c => c.Severity switch
            {
                "critical" => 4, "high" => 3, "medium" => 2, _ => 1
            }).ToList();
    }

    public async Task<FraudCase> SaveCaseAsync(FraudCase fraudCase)
    {
        fraudCase.LastSeen = DateTime.UtcNow;
        var all = await LoadAsync();
        all[fraudCase.Id] = fraudCase;
        await PersistAsync(all);
        logger.LogInformation("Case {Id} saved: {Title} [{Status}]",
            fraudCase.Id, fraudCase.Title, fraudCase.Status);
        return fraudCase;
    }

    public async Task<FraudCase?> GetCaseAsync(string id)
    {
        var all = await LoadAsync();
        return all.TryGetValue(id, out var c) ? c : null;
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

    public async Task DeleteCaseAsync(string id)
    {
        var all = await LoadAsync();
        all.Remove(id);
        await PersistAsync(all);
        logger.LogInformation("Case {Id} deleted", id);
    }

    /// <summary>
    /// Auto-resolves open cases that have not been updated in <paramref name="thresholdDays"/> days.
    /// Returns the number of cases resolved.
    /// </summary>
    public async Task<int> AutoResolveStaleAsync(int thresholdDays)
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromDays(thresholdDays);
        var stale = (await GetOpenCasesAsync()).Where(c => c.LastSeen < cutoff).ToList();

        foreach (var c in stale)
        {
            logger.LogWarning("Auto-resolving stale case {Id} ({Title}) — last seen {LastSeen:yyyy-MM-dd}",
                c.Id, c.Title, c.LastSeen);
            await ResolveCaseAsync(c.Id,
                $"Auto-resolved: no agent activity for {thresholdDays}+ days (last seen {c.LastSeen:yyyy-MM-dd HH:mm} UTC).");
        }

        return stale.Count;
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
