using Sentinel.Agent;
using Sentinel.Infrastructure;
using ZiggyCreatures.Caching.Fusion;

namespace Sentinel.Analytics;

public sealed class PanelResult
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Chart { get; init; }
    public int Span { get; init; } = 1;
    public string? Note { get; init; }
    public List<string> Columns { get; init; } = [];
    public List<Dictionary<string, string>> Rows { get; init; } = [];

    /// <summary>Set when the query failed. The panel renders an error card instead of a chart.</summary>
    public string? Error { get; init; }
}

public sealed class PlatformDashboard
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Database { get; init; }
    public required int Days { get; init; }
    public required List<PanelResult> Panels { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Runs the fixed analytics panels against ClickHouse and caches the results.
///
/// Panels run concurrently and fail independently — one bad query renders as an error card
/// rather than taking the whole page down. Given these are hand-written queries against four
/// schemas, partial failure is the expected state, not an edge case.
/// </summary>
public class AnalyticsDashboardService(
    ClickHouseClient clickHouse,
    IFusionCache cache,
    ILogger<AnalyticsDashboardService> logger)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public async Task<PlatformDashboard?> GetAsync(string platformKey, int? requestedDays)
    {
        var platform = AnalyticsPanels.Find(platformKey);
        if (platform == null) return null;

        var days = AnalyticsPanels.ClampDays(requestedDays);

        return await cache.GetOrSetAsync(
            $"sentinel:analytics:{platform.Key}:{days}",
            _ => BuildAsync(platform, days),
            options => options.SetDuration(CacheDuration));
    }

    private async Task<PlatformDashboard> BuildAsync(AnalyticsPlatform platform, int days)
    {
        var results = await Task.WhenAll(platform.Panels.Select(p => RunPanelAsync(p, days)));

        return new PlatformDashboard
        {
            Key = platform.Key,
            Label = platform.Label,
            Database = platform.Database,
            Days = days,
            Panels = [.. results]
        };
    }

    private async Task<PanelResult> RunPanelAsync(AnalyticsPanel panel, int days)
    {
        // days is clamped to an int range before it ever reaches here, so this substitution
        // cannot carry user input into the query.
        var sql = panel.Sql.Replace("{days}", days.ToString());

        try
        {
            var raw = await clickHouse.QueryAsync(sql);

            if (raw.StartsWith("ClickHouse error") || raw.StartsWith("Error:") || raw.StartsWith("Query failed:"))
            {
                logger.LogWarning("Analytics panel {PanelId} failed: {Error}", panel.Id, raw);
                return Failed(panel, raw);
            }

            var table = AnalyticsAgentCore.ParseQueryResult(raw);
            return new PanelResult
            {
                Id = panel.Id,
                Title = panel.Title,
                Chart = panel.Chart,
                Span = panel.Span,
                Note = panel.Note,
                Columns = table.Columns,
                Rows = table.Rows
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Analytics panel {PanelId} threw", panel.Id);
            return Failed(panel, ex.Message);
        }
    }

    private static PanelResult Failed(AnalyticsPanel panel, string error) => new()
    {
        Id = panel.Id,
        Title = panel.Title,
        Chart = panel.Chart,
        Span = panel.Span,
        Note = panel.Note,
        Error = error.Length > 400 ? error[..400] : error
    };
}
