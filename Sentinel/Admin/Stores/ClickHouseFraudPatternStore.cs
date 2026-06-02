using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

/// <summary>
/// ClickHouse EF Core-backed fraud pattern store.
/// Table: sentinel.fraud_patterns (ReplacingMergeTree, ordered by id).
/// </summary>
public class ClickHouseFraudPatternStore(
    IDbContextFactory<SentinelClickHouseContext> dbFactory,
    ILogger<ClickHouseFraudPatternStore> logger)
    : IFraudPatternStore
{
    public async Task<List<FraudPatternEntity>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.FraudPatterns
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .ToListAsync();
    }

    public async Task<List<FraudPatternEntity>> GetEnabledAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.FraudPatterns
            .AsNoTracking()
            .Where(p => p.Enabled)
            .OrderBy(p => p.Id)
            .ToListAsync();
    }

    public async Task<List<FraudPatternEntity>> GetEnabledForWorkflowAsync(string workflowId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.FraudPatterns
            .AsNoTracking()
            .Where(p =>
                p.Enabled &&
                (p.WorkflowId == null || p.WorkflowId == "" || p.WorkflowId == (workflowId ?? "")))
            .OrderBy(p => p.Id)
            .ToListAsync();
    }

    public async Task<List<FraudPatternEntity>> GetByWorkflowAsync(string workflowId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.FraudPatterns
            .AsNoTracking()
            .Where(p => p.WorkflowId == (workflowId ?? ""))
            .OrderBy(p => p.Id)
            .ToListAsync();
    }

    public async Task<FraudPatternEntity?> GetByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.FraudPatterns
            .AsNoTracking()
            .Where(p => p.Id == id)
            .FirstOrDefaultAsync();
    }

    public async Task UpsertAsync(FraudPatternEntity pattern)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        pattern.UpdatedAt = DateTime.UtcNow;
        if (pattern.CreatedAt == default) pattern.CreatedAt = DateTime.UtcNow;
        db.ChangeTracker.Clear();
        db.FraudPatterns.Add(pattern);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync(
            $"ALTER TABLE sentinel.fraud_patterns DELETE WHERE id = {id}");
    }

    public async Task SeedDefaultsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var count = await db.FraudPatterns.CountAsync();
        if (count > 0)
        {
            await db.Database.ExecuteSqlRawAsync($"""
                ALTER TABLE sentinel.fraud_patterns
                UPDATE workflow_id = '{WorkflowDefaults.FraudRunWorkflowId}'
                WHERE workflow_id = ''
                """);
            return;
        }

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
