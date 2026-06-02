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
        var rows = await db.FraudPatterns
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .ToListAsync();

        return CollapseLatest(rows)
            .OrderBy(p => p.Id)
            .ToList();
    }

    public async Task<List<FraudPatternEntity>> GetEnabledAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.FraudPatterns
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .ToListAsync();

        return CollapseLatest(rows)
            .Where(p => p.Enabled)
            .OrderBy(p => p.Id)
            .ToList();
    }

    public async Task<List<FraudPatternEntity>> GetEnabledForWorkflowAsync(string workflowId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.FraudPatterns
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .ToListAsync();

        var targetWorkflowId = workflowId ?? "";
        return CollapseLatest(rows)
            .Where(p =>
                p.Enabled &&
                (string.IsNullOrEmpty(p.WorkflowId) || p.WorkflowId == targetWorkflowId))
            .OrderBy(p => p.Id)
            .ToList();
    }

    public async Task<List<FraudPatternEntity>> GetByWorkflowAsync(string workflowId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.FraudPatterns
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .ToListAsync();

        var targetWorkflowId = workflowId ?? "";
        return CollapseLatest(rows)
            .Where(p => (p.WorkflowId ?? "") == targetWorkflowId)
            .OrderBy(p => p.Id)
            .ToList();
    }

    public async Task<FraudPatternEntity?> GetByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.FraudPatterns
            .AsNoTracking()
            .Where(p => p.Id == id)
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync();

        return rows
            .OrderByDescending(p => p.UpdatedAt)
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefault();
    }

    public async Task UpsertAsync(FraudPatternEntity pattern)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        pattern.UpdatedAt = now;
        if (pattern.CreatedAt == default) pattern.CreatedAt = now;

        var existing = await db.FraudPatterns
            .AsNoTracking()
            .Where(p => p.Id == pattern.Id)
            .OrderByDescending(p => p.UpdatedAt)
            .FirstOrDefaultAsync();

        if (existing is null)
        {
            db.ChangeTracker.Clear();
            db.FraudPatterns.Add(pattern);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return;
        }

        pattern.CreatedAt = existing.CreatedAt == default ? pattern.CreatedAt : existing.CreatedAt;
        pattern.CreatedBy = string.IsNullOrWhiteSpace(existing.CreatedBy) ? pattern.CreatedBy : existing.CreatedBy;
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

    private static string Esc(string? s) => (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
    private static IEnumerable<FraudPatternEntity> CollapseLatest(IEnumerable<FraudPatternEntity> rows) => rows
        .GroupBy(p => p.Id)
        .Select(g => g.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.CreatedAt).First());
}
