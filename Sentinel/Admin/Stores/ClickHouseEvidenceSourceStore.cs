using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;
using Sentinel.Agent;

namespace Sentinel.Admin.Stores;

public class ClickHouseEvidenceSourceStore(SentinelClickHouseContext db, ILogger<ClickHouseEvidenceSourceStore> logger)
    : IEvidenceSourceStore
{
    public async Task<List<EvidenceSource>> GetAllAsync() =>
        await db.EvidenceSources.OrderBy(e => e.Id).ToListAsync();

    public async Task<List<EvidenceSource>> GetEnabledAsync() =>
        await db.EvidenceSources.Where(e => e.Enabled).OrderBy(e => e.Id).ToListAsync();

    public async Task<EvidenceSource?> GetByIdAsync(int id) =>
        await db.EvidenceSources.FirstOrDefaultAsync(e => e.Id == id);

    public async Task UpsertAsync(EvidenceSource source)
    {
        source.UpdatedAt = DateTime.UtcNow;
        var existing = await db.EvidenceSources.FirstOrDefaultAsync(e => e.Id == source.Id);
        if (existing is not null)
        {
            existing.Name = source.Name;
            existing.EvidenceDatabase = source.EvidenceDatabase;
            existing.LipilaMerchantIds = source.LipilaMerchantIds;
            existing.LipilaPartnerId = source.LipilaPartnerId;
            existing.JoinMappings = source.JoinMappings;
            existing.TableDescriptions = source.TableDescriptions;
            existing.EvidenceChecks = source.EvidenceChecks;
            existing.Notes = source.Notes;
            existing.Enabled = source.Enabled;
            existing.UpdatedAt = source.UpdatedAt;
            existing.CreatedBy = source.CreatedBy;
        }
        else
        {
            db.EvidenceSources.Add(source);
        }
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await db.EvidenceSources.FirstOrDefaultAsync(e => e.Id == id);
        if (entity is not null)
        {
            db.EvidenceSources.Remove(entity);
            await db.SaveChangesAsync();
        }
    }

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
