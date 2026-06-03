using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;

namespace Sentinel.Admin.Services;

/// <summary>
/// On first startup, when both ClickHouse and PostgreSQL are configured,
/// copies all existing ClickHouse data to PostgreSQL so no records are lost.
/// Safe to run multiple times — skips tables that already have rows.
/// </summary>
public class ClickHouseToPostgresMigrationService(
    IServiceScopeFactory scopeFactory,
    ILogger<ClickHouseToPostgresMigrationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var pgFactory = scope.ServiceProvider.GetService<IDbContextFactory<SentinelDbContext>>();
        var chFactory = scope.ServiceProvider.GetService<IDbContextFactory<SentinelClickHouseContext>>();

        if (pgFactory is null || chFactory is null)
        {
            logger.LogDebug("Skipping ClickHouse→Postgres migration: one or both contexts not registered.");
            return;
        }

        await using var pg = await pgFactory.CreateDbContextAsync(cancellationToken);
        await using var ch = await chFactory.CreateDbContextAsync(cancellationToken);

        logger.LogInformation("Checking if ClickHouse→Postgres data migration is needed...");

        await MigrateWorkflows(pg, ch, cancellationToken);
        await MigrateFraudPatterns(pg, ch, cancellationToken);
        await MigrateEvidenceSources(pg, ch, cancellationToken);
        await MigrateRunSummaries(pg, ch, cancellationToken);
        await MigrateRunLogs(pg, ch, cancellationToken);
        await MigrateAuditLogs(pg, ch, cancellationToken);

        logger.LogInformation("ClickHouse→Postgres migration complete.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ── Workflows ────────────────────────────────────────────────────────────

    private async Task MigrateWorkflows(SentinelDbContext pg, SentinelClickHouseContext ch, CancellationToken ct)
    {
        if (await pg.Workflows.AnyAsync(ct))
        {
            logger.LogDebug("Workflows already present in Postgres, skipping.");
            return;
        }

        var rows = await ch.Workflows.AsNoTracking()
            .OrderByDescending(w => w.UpdatedAt).ToListAsync(ct);

        // Collapse to latest version per ID (append-only ClickHouse pattern)
        var latest = rows
            .GroupBy(w => w.Id, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(x => x.UpdatedAt).First())
            .Where(w => !w.IsDeleted)
            .ToList();

        if (latest.Count == 0)
        {
            logger.LogDebug("No workflows found in ClickHouse.");
            return;
        }

        pg.Workflows.AddRange(latest);
        await pg.SaveChangesAsync(ct);
        logger.LogInformation("Migrated {Count} workflows from ClickHouse to Postgres", latest.Count);
    }

    // ── Fraud Patterns ───────────────────────────────────────────────────────

    private async Task MigrateFraudPatterns(SentinelDbContext pg, SentinelClickHouseContext ch, CancellationToken ct)
    {
        if (await pg.FraudPatterns.AnyAsync(ct))
        {
            logger.LogDebug("Fraud patterns already present in Postgres, skipping.");
            return;
        }

        var rows = await ch.FraudPatterns.AsNoTracking().ToListAsync(ct);
        var latest = rows
            .GroupBy(p => p.Id)
            .Select(g => g.OrderByDescending(x => x.UpdatedAt).First())
            .ToList();

        if (latest.Count == 0)
        {
            logger.LogDebug("No fraud patterns found in ClickHouse.");
            return;
        }

        // Reset EF-tracked IDs so Postgres SERIAL generates them
        foreach (var p in latest)
        {
            p.CreatedAt = p.CreatedAt == default ? DateTimeOffset.UtcNow : p.CreatedAt;
            p.UpdatedAt = p.UpdatedAt == default ? DateTimeOffset.UtcNow : p.UpdatedAt;
        }

        pg.FraudPatterns.AddRange(latest);
        await pg.SaveChangesAsync(ct);
        logger.LogInformation("Migrated {Count} fraud patterns from ClickHouse to Postgres", latest.Count);
    }

    // ── Evidence Sources ─────────────────────────────────────────────────────

    private async Task MigrateEvidenceSources(SentinelDbContext pg, SentinelClickHouseContext ch, CancellationToken ct)
    {
        if (await pg.EvidenceSources.AnyAsync(ct))
        {
            logger.LogDebug("Evidence sources already present in Postgres, skipping.");
            return;
        }

        var rows = await ch.EvidenceSources.AsNoTracking().ToListAsync(ct);
        var latest = rows
            .GroupBy(s => s.Id)
            .Select(g => g.OrderByDescending(x => x.UpdatedAt).First())
            .ToList();

        if (latest.Count == 0)
        {
            logger.LogDebug("No evidence sources found in ClickHouse.");
            return;
        }

        foreach (var s in latest)
        {
            s.CreatedAt = s.CreatedAt == default ? DateTimeOffset.UtcNow : s.CreatedAt;
            s.UpdatedAt = s.UpdatedAt == default ? DateTimeOffset.UtcNow : s.UpdatedAt;
        }

        pg.EvidenceSources.AddRange(latest);
        await pg.SaveChangesAsync(ct);
        logger.LogInformation("Migrated {Count} evidence sources from ClickHouse to Postgres", latest.Count);
    }

    // ── Run Summaries ────────────────────────────────────────────────────────

    private async Task MigrateRunSummaries(SentinelDbContext pg, SentinelClickHouseContext ch, CancellationToken ct)
    {
        if (await pg.RunSummaries.AnyAsync(ct))
        {
            logger.LogDebug("Run summaries already present in Postgres, skipping.");
            return;
        }

        var rows = await ch.RunSummaries.AsNoTracking()
            .OrderByDescending(r => r.StartedAt).ToListAsync(ct);

        if (rows.Count == 0)
        {
            logger.LogDebug("No run summaries found in ClickHouse.");
            return;
        }

        // Deduplicate by RunId — ClickHouse may have multiple rows per run
        var latest = rows
            .GroupBy(r => r.RunId, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        pg.RunSummaries.AddRange(latest);
        await pg.SaveChangesAsync(ct);
        logger.LogInformation("Migrated {Count} run summaries from ClickHouse to Postgres", latest.Count);
    }

    // ── Run Logs ─────────────────────────────────────────────────────────────

    private async Task MigrateRunLogs(SentinelDbContext pg, SentinelClickHouseContext ch, CancellationToken ct)
    {
        if (await pg.RunLogs.AnyAsync(ct))
        {
            logger.LogDebug("Run logs already present in Postgres, skipping.");
            return;
        }

        var rows = await ch.RunLogs.AsNoTracking()
            .OrderBy(r => r.RunId).ThenBy(r => r.Iteration).ThenBy(r => r.StartedAt)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            logger.LogDebug("No run logs found in ClickHouse.");
            return;
        }

        // Deduplicate on composite key (RunId, StartedAt, ToolName) to match Postgres PK
        var deduped = rows
            .GroupBy(r => (r.RunId, r.StartedAt, r.ToolName))
            .Select(g => g.First())
            .ToList();

        // Insert in batches to avoid memory issues with large logs
        const int batchSize = 500;
        for (var i = 0; i < deduped.Count; i += batchSize)
        {
            pg.RunLogs.AddRange(deduped.Skip(i).Take(batchSize));
            await pg.SaveChangesAsync(ct);
            pg.ChangeTracker.Clear();
        }

        logger.LogInformation("Migrated {Count} run logs from ClickHouse to Postgres", deduped.Count);
    }

    // ── Audit Logs ───────────────────────────────────────────────────────────

    private async Task MigrateAuditLogs(SentinelDbContext pg, SentinelClickHouseContext ch, CancellationToken ct)
    {
        if (await pg.AuditLogs.AnyAsync(ct))
        {
            logger.LogDebug("Audit logs already present in Postgres, skipping.");
            return;
        }

        var rows = await ch.AuditLogs.AsNoTracking()
            .OrderBy(a => a.Timestamp).ToListAsync(ct);

        if (rows.Count == 0)
        {
            logger.LogDebug("No audit logs found in ClickHouse.");
            return;
        }

        const int batchSize = 500;
        for (var i = 0; i < rows.Count; i += batchSize)
        {
            pg.AuditLogs.AddRange(rows.Skip(i).Take(batchSize));
            await pg.SaveChangesAsync(ct);
            pg.ChangeTracker.Clear();
        }

        logger.LogInformation("Migrated {Count} audit logs from ClickHouse to Postgres", rows.Count);
    }
}
