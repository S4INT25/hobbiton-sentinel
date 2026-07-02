using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;
using Sentinel.Agent;

namespace Sentinel.Admin.Stores;

public class PostgresEvidenceSourceStore(
    IDbContextFactory<SentinelDbContext> dbFactory,
    ILogger<PostgresEvidenceSourceStore> logger) : IEvidenceSourceStore
{
    public async Task<List<EvidenceSource>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.EvidenceSources.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
    }

    public async Task<List<EvidenceSource>> GetEnabledAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.EvidenceSources.AsNoTracking()
            .Where(s => s.Enabled).OrderBy(s => s.Id).ToListAsync();
    }

    public async Task<List<EvidenceSource>> GetEnabledForWorkflowAsync(string workflowId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.EvidenceSources.AsNoTracking()
            .Where(s => s.Enabled && (s.WorkflowId == null || s.WorkflowId == "" || s.WorkflowId == workflowId))
            .OrderBy(s => s.Id).ToListAsync();
    }

    public async Task<List<EvidenceSource>> GetByWorkflowAsync(string workflowId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.EvidenceSources.AsNoTracking()
            .Where(s => (s.WorkflowId ?? "") == workflowId)
            .OrderBy(s => s.Id).ToListAsync();
    }

    public async Task<EvidenceSource?> GetByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.EvidenceSources.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task UpsertAsync(EvidenceSource source)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        source.UpdatedAt = now;

        var existing = await db.EvidenceSources.FirstOrDefaultAsync(s => s.Id == source.Id);
        if (existing is null)
        {
            source.CreatedAt = now;
            db.EvidenceSources.Add(source);
        }
        else
        {
            existing.Name = source.Name;
            existing.EvidenceDatabase = source.EvidenceDatabase;
            existing.LipilaMerchantIds = source.LipilaMerchantIds;
            existing.LipilaPartnerId = source.LipilaPartnerId;
            existing.JoinMappings = source.JoinMappings;
            existing.TableDescriptions = source.TableDescriptions;
            existing.EvidenceChecks = source.EvidenceChecks;
            existing.Notes = source.Notes;
            existing.WorkflowId = source.WorkflowId;
            existing.Enabled = source.Enabled;
            existing.UpdatedAt = now;
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var source = await db.EvidenceSources.FirstOrDefaultAsync(s => s.Id == id);
        if (source is null) return;
        db.EvidenceSources.Remove(source);
        await db.SaveChangesAsync();
    }

    public async Task SeedDefaultsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var count = await db.EvidenceSources.CountAsync();
        if (count > 0) return;

        foreach (var source in EvidenceSourceDefaults.GetDefaults())
        {
            source.WorkflowId = WorkflowDefaults.FraudRunWorkflowId;
            db.EvidenceSources.Add(source);
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} default evidence sources", EvidenceSourceDefaults.GetDefaults().Count);
    }
}