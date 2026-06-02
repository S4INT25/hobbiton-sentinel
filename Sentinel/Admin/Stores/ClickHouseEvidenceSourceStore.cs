using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;
using Sentinel.Agent;

namespace Sentinel.Admin.Stores;

public class ClickHouseEvidenceSourceStore(
    IDbContextFactory<SentinelClickHouseContext> dbFactory,
    ILogger<ClickHouseEvidenceSourceStore> logger)
    : IEvidenceSourceStore
{
    public async Task<List<EvidenceSource>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.EvidenceSources
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .ToListAsync();

        return CollapseLatest(rows)
            .OrderBy(s => s.Id)
            .ToList();
    }

    public async Task<List<EvidenceSource>> GetEnabledAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.EvidenceSources
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .ToListAsync();

        return CollapseLatest(rows)
            .Where(s => s.Enabled)
            .OrderBy(s => s.Id)
            .ToList();
    }

    public async Task<List<EvidenceSource>> GetEnabledForWorkflowAsync(string workflowId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.EvidenceSources
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .ToListAsync();

        var targetWorkflowId = workflowId ?? "";
        return CollapseLatest(rows)
            .Where(s =>
                s.Enabled &&
                (string.IsNullOrEmpty(s.WorkflowId) || s.WorkflowId == targetWorkflowId))
            .OrderBy(s => s.Id)
            .ToList();
    }

    public async Task<List<EvidenceSource>> GetByWorkflowAsync(string workflowId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.EvidenceSources
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .ToListAsync();

        var targetWorkflowId = workflowId ?? "";
        return CollapseLatest(rows)
            .Where(s => (s.WorkflowId ?? "") == targetWorkflowId)
            .OrderBy(s => s.Id)
            .ToList();
    }

    public async Task<EvidenceSource?> GetByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.EvidenceSources
            .AsNoTracking()
            .Where(s => s.Id == id)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();

        return rows
            .OrderByDescending(s => s.UpdatedAt)
            .ThenByDescending(s => s.CreatedAt)
            .FirstOrDefault();
    }

    public async Task UpsertAsync(EvidenceSource source)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        source.UpdatedAt = now;
        if (source.CreatedAt == default) source.CreatedAt = now;

        var existing = await db.EvidenceSources
            .AsNoTracking()
            .Where(s => s.Id == source.Id)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync();

        if (existing is null)
        {
            db.ChangeTracker.Clear();
            db.EvidenceSources.Add(source);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return;
        }

        source.CreatedAt = existing.CreatedAt == default ? source.CreatedAt : existing.CreatedAt;
        source.CreatedBy = string.IsNullOrWhiteSpace(existing.CreatedBy) ? source.CreatedBy : existing.CreatedBy;
        db.ChangeTracker.Clear();
        db.EvidenceSources.Add(source);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync(
            $"ALTER TABLE sentinel.evidence_sources DELETE WHERE id = {id}");
    }

    public async Task SeedDefaultsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
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

    private static string Esc(string? s) => (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
    private static IEnumerable<EvidenceSource> CollapseLatest(IEnumerable<EvidenceSource> rows) => rows
        .GroupBy(s => s.Id)
        .Select(g => g.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.CreatedAt).First());
}
