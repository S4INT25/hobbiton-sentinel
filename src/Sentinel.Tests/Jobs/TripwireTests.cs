using System.Reflection;
using Hangfire;
using Sentinel.Jobs;

namespace Sentinel.Tests.Jobs;

/// <summary>
/// The tripwire scanner's two silent-failure modes, both of which look exactly like "all clear":
/// a query error parsed as zero hits, and a rule written without the PeerDB filters so it matches
/// deleted or duplicated rows. Nothing in production surfaces either one — no alert is indis-
/// tinguishable from no fraud — so they get caught here or not at all.
/// </summary>
public class TripwireTests
{
    [Fact]
    public void TryParseRows_ReadsClickHouseHits()
    {
        const string json = """
                            {"meta":[{"name":"entity_key","type":"String"}],
                             "data":[{"entity_key":"1930","merchant_name":"WAITAPP","txn_count":"6"}],
                             "rows":1}
                            """;

        Assert.True(TripwireScanJob.TryParseRows(json, out var rows));
        var row = Assert.Single(rows);
        Assert.Equal("1930", row["entity_key"].ToString());
        Assert.Equal("WAITAPP", row["merchant_name"].ToString());
    }

    [Fact]
    public void TryParseRows_EmptyResultIsSuccessNotFailure()
    {
        Assert.True(TripwireScanJob.TryParseRows("""{"meta":[],"data":[],"rows":0}""", out var rows));
        Assert.Empty(rows);
    }

    [Theory]
    // ClickHouseClient swallows failures and returns these as plain strings. Parsed as "no hits",
    // a broken rule would report all-clear forever.
    [InlineData("Error: Only SELECT/WITH/SHOW/DESCRIBE queries are permitted.")]
    [InlineData("ClickHouse error (BadRequest): Code: 47. Unknown identifier")]
    [InlineData("Query failed: Connection refused")]
    [InlineData("")]
    [InlineData("{not json")]
    public void TryParseRows_RejectsErrorResponses(string raw)
    {
        Assert.False(TripwireScanJob.TryParseRows(raw, out var rows));
        Assert.Empty(rows);
    }

    public static TheoryData<string> RuleNames() => [.. Tripwires.All.Select(t => t.Name)];

    [Theory]
    [MemberData(nameof(RuleNames))]
    public void EveryRule_IsScannable(string ruleName)
    {
        var rule = Tripwires.All.Single(t => t.Name == ruleName);

        // The scanner keys cooldown off this column; without it the rule fires and is then
        // dropped, every minute, with only a log line to show for it.
        Assert.Contains("entity_key", rule.Sql);

        // Mandatory on every PeerDB replica table. FINAL collapses unmerged ReplacingMergeTree
        // duplicates; _peerdb_is_deleted excludes rows deleted in Postgres. Missing either is
        // wrong silently, never loudly.
        Assert.Contains("FINAL", rule.Sql);
        Assert.Contains("_peerdb_is_deleted = 0", rule.Sql);

        // A window shorter than the replication lag lets rows land after the scan that would have
        // caught them and before the next one starts — a permanent, invisible hole.
        Assert.Contains($"INTERVAL {Tripwires.LookbackMinutes} MINUTE", rule.Sql);

        // No cooldown means one compromised merchant pages the on-call every minute until muted.
        Assert.True(rule.CooldownHours > 0, $"{rule.Name} has no cooldown");
    }

    [Fact]
    public void IngestionLag_ParsesFromClickHouseResponse()
    {
        const string json = """{"meta":[{"name":"lag_seconds"}],"data":[{"lag_seconds":"62"}],"rows":1}""";

        Assert.True(TripwireScanJob.TryParseRows(json, out var rows));
        Assert.True(long.TryParse(rows[0]["lag_seconds"].ToString(), out var lag));
        Assert.Equal(62, lag);
    }

    [Fact]
    public void IngestionLagCheck_IsNotATripwire()
    {
        // It watches the pipeline, not the money. If it ever became a rule in All, a stalled
        // replica would enqueue an LLM investigation every minute to rediscover that the
        // replica is stalled.
        Assert.DoesNotContain(Tripwires.All, t => t.Sql.Contains("lag_seconds"));
        Assert.Contains("max(created_at)", Tripwires.IngestionLagSql);
    }

    [Fact]
    public void Scans_AndAgentRuns_UseSeparateQueues()
    {
        // Scans run on their own Hangfire server. If they shared the fraud queue again they would
        // wait behind agent runs for minutes; if agent runs landed on the tripwire queue they would
        // block the scans from the other side. Either way the one-minute detection is gone.
        static string? QueueOf<T>() => typeof(T).GetCustomAttribute<QueueAttribute>()?.Queue;

        Assert.Equal(TripwireScanJob.Queue, QueueOf<TripwireScanJob>());
        Assert.Equal("fraud", QueueOf<SentinelJob>());
        Assert.NotEqual(QueueOf<SentinelJob>(), QueueOf<TripwireScanJob>());
    }

    [Fact]
    public void RuleNames_AreUnique()
    {
        // Names are the cooldown key. Two rules sharing one would suppress each other's alerts.
        var names = Tripwires.All.Select(t => t.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
