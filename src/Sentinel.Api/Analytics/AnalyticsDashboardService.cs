using System.Globalization;
using Sentinel.Agent;
using Sentinel.Infrastructure;
using ZiggyCreatures.Caching.Fusion;

namespace Sentinel.Analytics;

/// <summary>
/// One metric's total this period against the same-length period immediately before it.
/// </summary>
/// <param name="Column">The numeric column being compared.</param>
/// <param name="Current">Total over the selected window.</param>
/// <param name="Previous">Total over the preceding window of equal length.</param>
/// <param name="ChangePercent">
/// Percentage change, or null when the previous period was zero — a jump from nothing is not a
/// percentage, and rendering one ("+∞%", "+100%") would be a fabricated number.
/// </param>
public sealed record PanelComparison(
    string Column,
    double Current,
    double Previous,
    double? ChangePercent);

public sealed class PanelResult
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Chart { get; init; }
    public int Span { get; init; } = 1;
    public string? Note { get; init; }
    public List<string> Columns { get; init; } = [];
    public List<Dictionary<string, string>> Rows { get; init; } = [];

    /// <summary>Per-column period-over-period deltas. Empty when the panel opts out of comparison.</summary>
    public List<PanelComparison> Comparisons { get; init; } = [];

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

    /// <summary>
    /// Window bounds for a period ending <paramref name="periodsBack"/> windows ago.
    /// 0 is the current window, 1 the one immediately before it. Bounds are whole days —
    /// running to now() would leave a partial final day that reads as a collapse on a trend line.
    /// </summary>
    private static (string From, string To) Window(int days, int periodsBack)
    {
        var from = days * (periodsBack + 1);
        var to = days * periodsBack;
        return (
            $"toStartOfDay(now()) - INTERVAL {from} DAY",
            to == 0 ? "toStartOfDay(now())" : $"toStartOfDay(now()) - INTERVAL {to} DAY");
    }

    private async Task<PanelResult> RunPanelAsync(AnalyticsPanel panel, int days)
    {
        try
        {
            var current = await QueryWindowAsync(panel, days, periodsBack: 0);
            if (current.Error != null)
            {
                logger.LogWarning("Analytics panel {PanelId} failed: {Error}", panel.Id, current.Error);
                return Failed(panel, current.Error);
            }

            var comparisons = panel.Compare
                ? await BuildComparisonsAsync(panel, days, current.Table!)
                : [];

            return new PanelResult
            {
                Id = panel.Id,
                Title = panel.Title,
                Chart = panel.Chart,
                Span = panel.Span,
                Note = panel.Note,
                Columns = current.Table!.Columns,
                Rows = current.Table.Rows,
                Comparisons = comparisons
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Analytics panel {PanelId} threw", panel.Id);
            return Failed(panel, ex.Message);
        }
    }

    private async Task<(TableData? Table, string? Error)> QueryWindowAsync(
        AnalyticsPanel panel, int days, int periodsBack)
    {
        // days is clamped to an int range before it reaches here, so these substitutions cannot
        // carry user input into the query.
        var (from, to) = Window(days, periodsBack);
        var sql = panel.Sql.Replace("{from}", from).Replace("{to}", to);

        var raw = await clickHouse.QueryAsync(sql);
        if (raw.StartsWith("ClickHouse error") || raw.StartsWith("Error:") || raw.StartsWith("Query failed:"))
            return (null, raw);

        return (AnalyticsAgentCore.ParseQueryResult(raw), null);
    }

    /// <summary>
    /// Totals each numeric column over the current and preceding window. A failed comparison
    /// query is swallowed: the panel is still useful without a delta, and losing the whole card
    /// because the historical half timed out would be a worse trade.
    /// </summary>
    private async Task<List<PanelComparison>> BuildComparisonsAsync(
        AnalyticsPanel panel, int days, TableData current)
    {
        var previous = await QueryWindowAsync(panel, days, periodsBack: 1);
        if (previous.Error != null)
        {
            logger.LogWarning("Analytics panel {PanelId} comparison failed: {Error}", panel.Id, previous.Error);
            return [];
        }

        // First column is the label axis (the day); everything after it is a measure.
        return [.. current.Columns.Skip(1)
            .Select(c => new { Column = c, Cur = SumColumn(current, c), Prev = SumColumn(previous.Table!, c) })
            .Where(x => x.Cur.HasValue && x.Prev.HasValue)
            .Select(x => new PanelComparison(
                x.Column,
                x.Cur!.Value,
                x.Prev!.Value,
                x.Prev.Value == 0 ? null : (x.Cur.Value - x.Prev.Value) / Math.Abs(x.Prev.Value) * 100))];
    }

    /// <summary>Sums a column, or returns null if any value is non-numeric.</summary>
    private static double? SumColumn(TableData table, string column)
    {
        double total = 0;
        foreach (var row in table.Rows)
        {
            if (!row.TryGetValue(column, out var raw)) return null;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)) return null;
            total += n;
        }
        return total;
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
