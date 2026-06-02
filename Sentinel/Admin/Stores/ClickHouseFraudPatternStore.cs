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
    // ReplacingMergeTree deduplicates on merge; FINAL forces deduplication on read.
    public async Task<List<FraudPatternEntity>> GetAllAsync() =>
        await db.FraudPatterns
            .FromSqlRaw("SELECT * FROM sentinel.fraud_patterns FINAL ORDER BY id")
            .ToListAsync();

    public async Task<List<FraudPatternEntity>> GetEnabledAsync() =>
        await db.FraudPatterns
            .FromSqlRaw("SELECT * FROM sentinel.fraud_patterns FINAL WHERE enabled = 1 ORDER BY id")
            .ToListAsync();

    public async Task<List<FraudPatternEntity>> GetEnabledForWorkflowAsync(string workflowId) =>
        await db.FraudPatterns
            .FromSqlRaw(
                "SELECT * FROM sentinel.fraud_patterns FINAL WHERE enabled = 1 AND (workflow_id = '' OR workflow_id = {0}) ORDER BY id",
                workflowId ?? "")
            .ToListAsync();

    public async Task<List<FraudPatternEntity>> GetByWorkflowAsync(string workflowId) =>
        await db.FraudPatterns
            .FromSqlRaw(
                "SELECT * FROM sentinel.fraud_patterns FINAL WHERE workflow_id = {0} ORDER BY id",
                workflowId ?? "")
            .ToListAsync();

    public async Task<FraudPatternEntity?> GetByIdAsync(int id) =>
        await db.FraudPatterns
            .FromSqlRaw($"SELECT * FROM sentinel.fraud_patterns FINAL WHERE id = {id}")
            .FirstOrDefaultAsync();

    public async Task UpsertAsync(FraudPatternEntity pattern)
    {
        pattern.UpdatedAt = DateTime.UtcNow;
        if (pattern.CreatedAt == default) pattern.CreatedAt = DateTime.UtcNow;
        // ClickHouse ReplacingMergeTree: INSERT a new row; the engine retains the latest version by updated_at.
        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO sentinel.fraud_patterns
                (id, name, description, category, enabled, workflow_id, created_at, updated_at, created_by)
            VALUES
                ({pattern.Id}, '{Esc(pattern.Name)}', '{Esc(pattern.Description)}',
                 '{Esc(pattern.Category)}', {(pattern.Enabled ? 1 : 0)},
                 '{Esc(pattern.WorkflowId ?? "")}',
                 '{pattern.CreatedAt:yyyy-MM-dd HH:mm:ss}', '{pattern.UpdatedAt:yyyy-MM-dd HH:mm:ss}',
                 '{Esc(pattern.CreatedBy)}')
            """);
    }

    public async Task DeleteAsync(int id)
    {
        await db.Database.ExecuteSqlRawAsync(
            $"ALTER TABLE sentinel.fraud_patterns DELETE WHERE id = {id}");
    }

    private static string Esc(string? s) => (s ?? "").Replace("'", "\\'").Replace("\\", "\\\\");

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
                    workflow_id String DEFAULT '',
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
                CreatedBy = "system",
                WorkflowId = WorkflowDefaults.FraudRunWorkflowId
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
        logger.LogInformation("Seeded {Count} default fraud patterns to ClickHouse", defaults.Count());
    }
}
