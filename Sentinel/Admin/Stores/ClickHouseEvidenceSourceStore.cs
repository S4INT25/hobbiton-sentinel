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
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .ToListAsync();

    public async Task<List<EvidenceSource>> GetEnabledAsync() =>
        await db.EvidenceSources
            .AsNoTracking()
            .Where(s => s.Enabled)
            .OrderBy(s => s.Id)
            .ToListAsync();

    public async Task<List<EvidenceSource>> GetEnabledForWorkflowAsync(string workflowId) =>
        await db.EvidenceSources
            .AsNoTracking()
            .Where(s =>
                s.Enabled &&
                (s.WorkflowId == null || s.WorkflowId == "" || s.WorkflowId == (workflowId ?? "")))
            .OrderBy(s => s.Id)
            .ToListAsync();

    public async Task<List<EvidenceSource>> GetByWorkflowAsync(string workflowId) =>
        await db.EvidenceSources
            .AsNoTracking()
            .Where(s => s.WorkflowId == (workflowId ?? ""))
            .OrderBy(s => s.Id)
            .ToListAsync();

    public async Task<EvidenceSource?> GetByIdAsync(int id) =>
        await db.EvidenceSources
            .AsNoTracking()
            .Where(s => s.Id == id)
            .FirstOrDefaultAsync();

    public async Task UpsertAsync(EvidenceSource source)
    {
        source.UpdatedAt = DateTime.UtcNow;
        if (source.CreatedAt == default) source.CreatedAt = DateTime.UtcNow;
        db.ChangeTracker.Clear();
        db.EvidenceSources.Add(source);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    public async Task DeleteAsync(int id)
    {
        await db.Database.ExecuteSqlRawAsync(
            $"ALTER TABLE sentinel.evidence_sources DELETE WHERE id = {id}");
    }

    public async Task SeedDefaultsAsync()
    {
        var count = await db.EvidenceSources.CountAsync();
        if (count > 0)
        {
            await db.Database.ExecuteSqlRawAsync($"""
                ALTER TABLE sentinel.evidence_sources
                UPDATE workflow_id = '{WorkflowDefaults.FraudRunWorkflowId}'
                WHERE workflow_id = ''
                """);
            return;
        }

        foreach (var source in EvidenceSourceDefaults.GetDefaults())
        {
            source.WorkflowId = WorkflowDefaults.FraudRunWorkflowId;
            db.EvidenceSources.Add(source);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
        logger.LogInformation("Seeded {Count} default evidence sources to ClickHouse",
            EvidenceSourceDefaults.GetDefaults().Count);
    }
}
