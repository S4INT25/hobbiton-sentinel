using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Sentinel.Admin;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;
using Sentinel.Agent;
using Sentinel.Infrastructure;

namespace Sentinel.Jobs;

/// <summary>
/// The cheap half of fraud detection. Runs every minute, executes each tripwire's SQL against
/// ClickHouse, and on a hit wakes the LLM fraud agent scoped to that one entity.
///
/// Why this exists: the agent run is slow and expensive, so it can only be scheduled every few
/// hours — which means a pattern at 09:05 waits until 11:00. This job costs milliseconds, so it can
/// run continuously, and the agent now runs on real hits instead of on a timer.
/// </summary>
[Queue(Queue)]
public class TripwireScanJob(
    ClickHouseClient clickHouse,
    SentinelDbContext db,
    IActiveRunTracker runTracker,
    IBackgroundJobClient backgroundJobs,
    IConfiguration config,
    ILogger<TripwireScanJob> logger)
{
    /// <summary>Served by a dedicated Hangfire server so scans never queue behind agent runs.</summary>
    public const string Queue = "tripwire";

    private const string Database = "lipila_blaze";

    public async Task RunAsync()
    {
        var lagSeconds = await CheckIngestionLagAsync();

        var fired = 0;
        var failed = 0;

        foreach (var tripwire in Tripwires.All)
        {
            try
            {
                fired += await ScanAsync(tripwire);
            }
            catch (Exception ex)
            {
                failed++;
                // One broken rule must not take the other tripwires down with it — a scan that
                // dies silently is the worst outcome here, so log loudly and keep going.
                logger.LogError(ex, "Tripwire {Rule} failed to scan", tripwire.Name);
            }
        }

        // The heartbeat. A clean scan writes no alert row and logs nothing else, so without this
        // line "working perfectly" and "broken for a week" produce identical evidence.
        logger.LogInformation(
            "Tripwire scan complete: {Rules} rules, {Fired} fired, {Failed} failed, ingestion lag {Lag}s",
            Tripwires.All.Count, fired, failed, lagSeconds?.ToString() ?? "unknown");
    }

    /// <summary>
    /// Verifies the data is fresh enough for the scan to mean anything. Logged rather than
    /// escalated to the fraud agent: stalled replication is an ops failure, and an LLM
    /// investigation of it would burn a run to conclude "the pipeline is down".
    /// </summary>
    /// <returns>Lag in seconds, or null if it could not be determined.</returns>
    private async Task<long?> CheckIngestionLagAsync()
    {
        long? lagSeconds = null;

        try
        {
            var raw = await clickHouse.QueryAsync(Tripwires.IngestionLagSql);

            if (TryParseRows(raw, out var rows) && rows.Count > 0 &&
                rows[0].TryGetValue("lag_seconds", out var value) &&
                long.TryParse(value.ToString(), out var parsed))
                lagSeconds = parsed;
            else
                logger.LogError("Ingestion lag check could not be read: {Response}",
                    raw[..Math.Min(300, raw.Length)]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ingestion lag check failed");
        }

        var maxLag = config.GetValue("Sentinel:Tripwires:MaxLagSeconds", Tripwires.DefaultMaxLagSeconds);

        // Scanning continues either way — stale data still catches whatever did land, and refusing
        // to scan would trade one blind spot for a larger one.
        if (lagSeconds > maxLag)
            logger.LogError(
                "Ingestion lag is {Lag}s (limit {Max}s) — tripwires are scanning stale data and "
                + "their silence does not mean there is no fraud",
                lagSeconds, maxLag);

        return lagSeconds;
    }

    /// <returns>How many alerts this rule fired.</returns>
    private async Task<int> ScanAsync(Tripwire tripwire)
    {
        var raw = await clickHouse.QueryAsync(tripwire.Sql);

        if (!TryParseRows(raw, out var rows))
        {
            logger.LogError("Tripwire {Rule} query failed: {Response}", tripwire.Name,
                raw[..Math.Min(300, raw.Length)]);
            return 0;
        }

        var fired = 0;

        foreach (var row in rows)
        {
            if (!row.TryGetValue("entity_key", out var keyElement))
            {
                logger.LogError("Tripwire {Rule} returned a row with no entity_key column", tripwire.Name);
                continue;
            }

            var entityKey = keyElement.ToString();
            if (string.IsNullOrWhiteSpace(entityKey)) continue;

            if (await IsInCooldownAsync(tripwire, entityKey))
            {
                logger.LogDebug("Tripwire {Rule} suppressed for {Entity} (cooldown)", tripwire.Name, entityKey);
                continue;
            }

            await FireAsync(tripwire, entityKey, row);
            fired++;
        }

        return fired;
    }

    private Task<bool> IsInCooldownAsync(Tripwire tripwire, string entityKey)
    {
        var since = DateTimeOffset.UtcNow.AddHours(-tripwire.CooldownHours);
        return db.TripwireAlerts.AnyAsync(a =>
            a.RuleName == tripwire.Name && a.EntityKey == entityKey && a.FiredAt > since);
    }

    private async Task FireAsync(Tripwire tripwire, string entityKey, Dictionary<string, JsonElement> row)
    {
        var detail = JsonSerializer.Serialize(row);
        var runId = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var triggeredBy = $"tripwire:{tripwire.Name}";

        // Recorded before enqueuing, not after: if the enqueue throws, the alert must still be on
        // record and in cooldown. Otherwise a broken queue turns into a fire-every-minute loop.
        var alert = new TripwireAlert
        {
            RuleName = tripwire.Name,
            EntityKey = entityKey,
            Detail = detail,
            RunId = runId
        };
        db.TripwireAlerts.Add(alert);
        await db.SaveChangesAsync();

        logger.LogWarning("Tripwire {Rule} fired for {Entity}: {Detail}", tripwire.Name, entityKey, detail);

        await runTracker.MarkQueuedAsync(runId, triggeredBy, DateTime.UtcNow);
        backgroundJobs.Enqueue<SentinelJob>(job => job.RunAsync(new FraudAgentRunRequest
        {
            TriggeredBy = triggeredBy,
            RunId = runId,
            Database = Database,
            CustomPrompt = BuildPrompt(tripwire, entityKey, detail)
        }));
    }

    /// <summary>
    /// Scopes the agent to the entity that tripped. The tripwire has already established that
    /// something happened, so the agent's job is confirmation and context — not another sweep.
    /// </summary>
    private static string BuildPrompt(Tripwire tripwire, string entityKey, string detail) =>
        $"""
         A deterministic tripwire fired. Investigate this specific entity — do not run a general sweep.

         Tripwire: {tripwire.Name}
         What it detects: {tripwire.Description}
         Entity: {entityKey}
         Matching rows (last {Tripwires.LookbackMinutes} minutes): {detail}

         Establish whether this is genuine fraud or expected behaviour. Specifically:
         - Pull this entity's recent transaction history and its 30-day baseline. Volume that is
           normal for this merchant is not a finding, even when it looks high in isolation.
         - Check the counterparties: are the recipients new, recurring, or shared with other merchants?
         - Explain how the transactions happened despite the control the tripwire is based on.

         If it is expected behaviour, say so plainly and close it — a clean explanation is a useful
         result. If it is genuine, raise a case with the evidence you found.
         """;

    /// <summary>
    /// ClickHouse returns <c>{"meta":[...],"data":[...]}</c> on success. ClickHouseClient swallows
    /// failures and hands back a plain error string, so a missing "data" array means the query
    /// failed — treated as an error rather than as "no hits", which would fail silently.
    /// </summary>
    public static bool TryParseRows(string raw, out List<Dictionary<string, JsonElement>> rows)
    {
        rows = [];
        if (string.IsNullOrWhiteSpace(raw) || !raw.TrimStart().StartsWith('{')) return false;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array) return false;

            rows = [.. data.EnumerateArray()
                .Where(r => r.ValueKind == JsonValueKind.Object)
                .Select(r => r.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()))];
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
