using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

/// <summary>
/// ClickHouse EF Core-backed fraud pattern store.
/// Table: sentinel.fraud_patterns (ReplacingMergeTree, ordered by id).
/// </summary>
public class ClickHouseFraudPatternStore(SentinelClickHouseContext db, ILogger<ClickHouseFraudPatternStore> logger)
    : IFraudPatternStore
{
    public async Task<List<FraudPatternEntity>> GetAllAsync() =>
        await db.FraudPatterns.OrderBy(p => p.Id).ToListAsync();

    public async Task<List<FraudPatternEntity>> GetEnabledAsync() =>
        await db.FraudPatterns.Where(p => p.Enabled).OrderBy(p => p.Id).ToListAsync();

    public async Task<FraudPatternEntity?> GetByIdAsync(int id) =>
        await db.FraudPatterns.FirstOrDefaultAsync(p => p.Id == id);

    public async Task UpsertAsync(FraudPatternEntity pattern)
    {
        pattern.UpdatedAt = DateTime.UtcNow;
        var existing = await db.FraudPatterns.FirstOrDefaultAsync(p => p.Id == pattern.Id);
        if (existing is not null)
        {
            existing.Name = pattern.Name;
            existing.Description = pattern.Description;
            existing.Category = pattern.Category;
            existing.Enabled = pattern.Enabled;
            existing.UpdatedAt = pattern.UpdatedAt;
            existing.CreatedBy = pattern.CreatedBy;
        }
        else
        {
            db.FraudPatterns.Add(pattern);
        }
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await db.FraudPatterns.FirstOrDefaultAsync(p => p.Id == id);
        if (entity is not null)
        {
            db.FraudPatterns.Remove(entity);
            await db.SaveChangesAsync();
        }
    }

    public async Task EnsureTableAsync()
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS sentinel.fraud_patterns (
                    id Int32,
                    name String,
                    description String,
                    category String DEFAULT 'TransactionAnomaly',
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
            logger.LogError(ex, "Failed to ensure fraud_patterns table");
        }
    }

    public async Task SeedDefaultsAsync()
    {
        var count = await db.FraudPatterns.CountAsync();
        if (count > 0) return;

        var defaults = Agent.FraudPatternRegistry.GetDefaults();
        foreach (var p in defaults)
        {
            db.FraudPatterns.Add(new FraudPatternEntity
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Category = p.Category.ToString(),
                Enabled = p.EnabledByDefault,
                CreatedBy = "system"
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
        logger.LogInformation("Seeded {Count} default fraud patterns to ClickHouse", defaults.Count());
    }
}
