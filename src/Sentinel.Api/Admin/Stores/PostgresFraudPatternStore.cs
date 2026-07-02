using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;
using Sentinel.Agent;

namespace Sentinel.Admin.Stores;

public class PostgresFraudPatternStore(
    IDbContextFactory<SentinelDbContext> dbFactory,
    ILogger<PostgresFraudPatternStore> logger) : IFraudPatternStore
{
    public async Task<List<FraudPatternEntity>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.FraudPatterns.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
    }

    public async Task<List<FraudPatternEntity>> GetEnabledAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.FraudPatterns.AsNoTracking()
            .Where(p => p.Enabled).OrderBy(p => p.Id).ToListAsync();
    }

    public async Task<List<FraudPatternEntity>> GetEnabledForWorkflowAsync(string workflowId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.FraudPatterns.AsNoTracking()
            .Where(p => p.Enabled && (p.WorkflowId == null || p.WorkflowId == "" || p.WorkflowId == workflowId))
            .OrderBy(p => p.Id).ToListAsync();
    }

    public async Task<List<FraudPatternEntity>> GetByWorkflowAsync(string workflowId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.FraudPatterns.AsNoTracking()
            .Where(p => (p.WorkflowId ?? "") == workflowId)
            .OrderBy(p => p.Id).ToListAsync();
    }

    public async Task<FraudPatternEntity?> GetByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.FraudPatterns.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task UpsertAsync(FraudPatternEntity pattern)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        pattern.UpdatedAt = now;

        var existing = await db.FraudPatterns.FirstOrDefaultAsync(p => p.Id == pattern.Id);
        if (existing is null)
        {
            pattern.CreatedAt = now;
            db.FraudPatterns.Add(pattern);
        }
        else
        {
            existing.Name = pattern.Name;
            existing.Description = pattern.Description;
            existing.Category = pattern.Category;
            existing.Enabled = pattern.Enabled;
            existing.WorkflowId = pattern.WorkflowId;
            existing.UpdatedAt = now;
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var pattern = await db.FraudPatterns.FirstOrDefaultAsync(p => p.Id == id);
        if (pattern is null) return;
        db.FraudPatterns.Remove(pattern);
        await db.SaveChangesAsync();
    }

    public async Task SeedDefaultsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var count = await db.FraudPatterns.CountAsync();
        if (count > 0) return;

        var defaults = FraudPatternRegistry.GetDefaults();
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
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} default fraud patterns", defaults.Count());
    }
}