using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;
using Sentinel.Agent;

namespace Sentinel.Admin.Stores;

public class ClickHouseEvidenceSourceStore(SentinelClickHouseContext db, ILogger<ClickHouseEvidenceSourceStore> logger)
    : IEvidenceSourceStore
{
    public async Task<List<EvidenceSource>> GetAllAsync() =>
        await db.EvidenceSources
            .FromSqlRaw("SELECT * FROM sentinel.evidence_sources FINAL ORDER BY id")
            .ToListAsync();

    public async Task<List<EvidenceSource>> GetEnabledAsync() =>
        await db.EvidenceSources
            .FromSqlRaw("SELECT * FROM sentinel.evidence_sources FINAL WHERE enabled = 1 ORDER BY id")
            .ToListAsync();

    public async Task<EvidenceSource?> GetByIdAsync(int id) =>
        await db.EvidenceSources
            .FromSqlRaw($"SELECT * FROM sentinel.evidence_sources FINAL WHERE id = {id}")
            .FirstOrDefaultAsync();

    public async Task UpsertAsync(EvidenceSource source)
    {
        source.UpdatedAt = DateTime.UtcNow;
        if (source.CreatedAt == default) source.CreatedAt = DateTime.UtcNow;
        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO sentinel.evidence_sources
                (id, name, evidence_database, lipila_merchant_ids, lipila_partner_id,
                 join_mappings, table_descriptions, evidence_checks, notes,
                 enabled, created_at, updated_at, created_by)
            VALUES
                ({source.Id}, '{Esc(source.Name)}', '{Esc(source.EvidenceDatabase)}',
                 '{Esc(source.LipilaMerchantIds)}', {source.LipilaPartnerId},
                 '{Esc(source.JoinMappings)}', '{Esc(source.TableDescriptions)}',
                 '{Esc(source.EvidenceChecks)}', '{Esc(source.Notes)}',
                 {(source.Enabled ? 1 : 0)},
                 '{source.CreatedAt:yyyy-MM-dd HH:mm:ss}', '{source.UpdatedAt:yyyy-MM-dd HH:mm:ss}',
                 '{Esc(source.CreatedBy)}')
            """);
    }

    public async Task DeleteAsync(int id)
    {
        await db.Database.ExecuteSqlRawAsync(
            $"ALTER TABLE sentinel.evidence_sources DELETE WHERE id = {id}");
    }

    private static string Esc(string? s) => (s ?? "").Replace("'", "\\'").Replace("\\", "\\\\");

    public async Task EnsureTableAsync()
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS sentinel.evidence_sources (
                    id Int32,
                    name String,
                    evidence_database String,
                    lipila_merchant_ids String DEFAULT '',
                    lipila_partner_id Int32 DEFAULT 0,
                    join_mappings String DEFAULT '[]',
                    table_descriptions String DEFAULT '',
                    evidence_checks String DEFAULT '[]',
                    notes String DEFAULT '',
                    enabled UInt8 DEFAULT 1,
                    created_at DateTime DEFAULT now(),
                    updated_at DateTime DEFAULT now(),
                    created_by String DEFAULT 'system'
                ) ENGINE = ReplacingMergeTree(updated_at)
                ORDER BY id
                """);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ensure evidence_sources table");
        }
    }

    public async Task SeedDefaultsAsync()
    {
        var count = await db.EvidenceSources.CountAsync();
        if (count > 0) return;

        foreach (var source in EvidenceSourceDefaults.GetDefaults())
        {
            db.EvidenceSources.Add(source);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
        logger.LogInformation("Seeded {Count} default evidence sources to ClickHouse",
            EvidenceSourceDefaults.GetDefaults().Count);
    }
}
