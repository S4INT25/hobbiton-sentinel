using Sentinel.Analytics;

namespace Sentinel.Tests.Analytics;

/// <summary>
/// The two ClickHouse house rules — <c>_peerdb_is_deleted = 0</c> and <c>FINAL</c> — fail
/// silently when forgotten: the query still returns, just with soft-deleted and superseded
/// rows counted in. A dashboard that is quietly wrong is worse than one that errors, so
/// these are asserted rather than left to review.
/// </summary>
public class AnalyticsPanelsTests
{
    public static TheoryData<string, string, string> AllPanels()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var platform in AnalyticsPanels.All)
            foreach (var panel in platform.Panels)
                data.Add(platform.Key, panel.Id, panel.Sql);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPanels))]
    public void Every_panel_filters_soft_deleted_rows(string platform, string id, string sql)
    {
        Assert.True(sql.Contains("_peerdb_is_deleted", StringComparison.Ordinal),
            $"{platform}/{id} does not filter _peerdb_is_deleted — it will count deleted rows.");
    }

    [Theory]
    [MemberData(nameof(AllPanels))]
    public void Every_panel_uses_FINAL(string platform, string id, string sql)
    {
        Assert.True(sql.Contains("FINAL", StringComparison.Ordinal),
            $"{platform}/{id} omits FINAL — ReplacingMergeTree will return superseded row versions.");
    }

    [Theory]
    [MemberData(nameof(AllPanels))]
    public void Every_panel_is_read_only(string platform, string id, string sql)
    {
        // Mirror ClickHouseClient's gate, which skips leading `--` comments before reading the
        // first keyword. Checking the raw first token instead would fail any commented query
        // that the client actually accepts.
        var trimmed = sql.TrimStart();
        while (trimmed.StartsWith("--", StringComparison.Ordinal))
        {
            var nl = trimmed.IndexOfAny(['\n', '\r']);
            if (nl < 0) break;
            trimmed = trimmed[(nl + 1)..].TrimStart();
        }

        var head = trimmed.Split(' ', '\n', '\r')[0].ToUpperInvariant();
        Assert.True(head is "SELECT" or "WITH", $"{platform}/{id} does not start with SELECT/WITH.");
    }

    [Theory]
    [MemberData(nameof(AllPanels))]
    public void Joined_panels_filter_soft_deletes_on_both_sides(string platform, string id, string sql)
    {
        if (!sql.Contains("JOIN", StringComparison.Ordinal)) return;

        // A join that filters only the left table silently readmits deleted rows from the right.
        var occurrences = sql.Split("_peerdb_is_deleted").Length - 1;
        Assert.True(occurrences >= 2,
            $"{platform}/{id} joins but filters _peerdb_is_deleted {occurrences} time(s) — needs one per table.");
    }

    [Fact]
    public void Panel_ids_are_unique_within_a_platform()
    {
        foreach (var platform in AnalyticsPanels.All)
        {
            var ids = platform.Panels.Select(p => p.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }
    }

    [Fact]
    public void Windowed_panels_use_the_days_placeholder()
    {
        // The portfolio snapshot is deliberately un-windowed; everything else must respect
        // the selected range or the window control silently does nothing.
        foreach (var platform in AnalyticsPanels.All)
            foreach (var panel in platform.Panels.Where(p => p.Id != "portfolio"))
                Assert.True(panel.Sql.Contains("{days}", StringComparison.Ordinal),
                    $"{platform.Key}/{panel.Id} ignores the {{days}} window.");
    }

    [Theory]
    [InlineData(null, 30)]
    [InlineData(30, 30)]
    [InlineData(7, 7)]
    [InlineData(180, 180)]
    [InlineData(0, 7)]
    [InlineData(-5, 7)]
    [InlineData(9999, 180)]
    public void ClampDays_keeps_the_window_in_range(int? input, int expected)
    {
        // days is interpolated straight into SQL, so the clamp is the injection guard too.
        Assert.Equal(expected, AnalyticsPanels.ClampDays(input));
    }

    [Fact]
    public void Platform_keys_are_unique_and_resolvable()
    {
        var keys = AnalyticsPanels.All.Select(p => p.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());

        foreach (var key in keys)
            Assert.NotNull(AnalyticsPanels.Find(key));

        Assert.NotNull(AnalyticsPanels.Find("LIPILA"));
        Assert.Null(AnalyticsPanels.Find("nope"));
        Assert.Null(AnalyticsPanels.Find(null));
    }

    [Fact]
    public void Chart_types_are_ones_the_frontend_renders()
    {
        string[] supported = ["line", "area", "bar", "donut", "table"];
        foreach (var platform in AnalyticsPanels.All)
            foreach (var panel in platform.Panels)
                Assert.Contains(panel.Chart, supported);
    }
}
