using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class ClickHouseWorkflowStore(SentinelClickHouseContext db, ILogger<ClickHouseWorkflowStore> logger) : IWorkflowStore
{
    public async Task<List<WorkflowDefinition>> GetAllAsync() =>
        await db.Workflows
            .AsNoTracking()
            .Where(w => !w.IsDeleted)
            .OrderByDescending(w => w.UpdatedAt)
            .ToListAsync();

    public async Task<List<WorkflowDefinition>> GetEnabledAsync() =>
        await db.Workflows
            .AsNoTracking()
            .Where(w => !w.IsDeleted && w.Enabled)
            .OrderByDescending(w => w.UpdatedAt)
            .ToListAsync();

    public async Task<WorkflowDefinition?> GetByIdAsync(string id) =>
        await db.Workflows
            .AsNoTracking()
            .Where(w => w.Id == id && !w.IsDeleted)
            .FirstOrDefaultAsync();

    public async Task UpsertAsync(WorkflowDefinition workflow)
    {
        NormalizeAndValidate(workflow);

        if (string.IsNullOrWhiteSpace(workflow.Id))
            workflow.Id = Guid.NewGuid().ToString("N");

        workflow.UpdatedAt = DateTime.UtcNow;
        if (workflow.CreatedAt == default)
            workflow.CreatedAt = DateTime.UtcNow;

        db.ChangeTracker.Clear();
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    public async Task DeleteAsync(string id)
    {
        await db.Database.ExecuteSqlRawAsync($"""
            ALTER TABLE sentinel.workflows
            UPDATE enabled = 0, is_deleted = 1, updated_at = now()
            WHERE id = '{Esc(id)}' AND is_deleted = 0
            """);
    }

    private static string Esc(string? s) => (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public async Task SeedDefaultsAsync()
    {
        var existing = await GetAllAsync();
        var now = DateTime.UtcNow;

        foreach (var workflow in WorkflowDefaults.All)
        {
            var exists = existing.Any(w =>
                string.Equals(w.Id, workflow.Id, StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(w.ActionType, workflow.ActionType, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(w.Name, workflow.Name, StringComparison.OrdinalIgnoreCase)));

            if (exists)
                continue;

            var seeded = new WorkflowDefinition
            {
                Id = workflow.Id,
                Name = workflow.Name,
                Description = workflow.Description,
                ActionType = workflow.ActionType,
                CronExpression = workflow.CronExpression,
                Enabled = workflow.Enabled,
                TargetDatabase = workflow.TargetDatabase,
                EmailSubject = workflow.EmailSubject,
                EmailRecipients = workflow.EmailRecipients,
                CustomPrompt = workflow.CustomPrompt,
                IsDeleted = false,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = workflow.CreatedBy
            };

            await UpsertAsync(seeded);
            logger.LogInformation("Seeded default workflow {WorkflowId} ({WorkflowName})", seeded.Id, seeded.Name);
        }
    }

    private static void NormalizeAndValidate(WorkflowDefinition workflow)
    {
        workflow.ActionType = WorkflowActionTypes.Normalize(workflow.ActionType);

        if (!WorkflowActionTypes.All.Contains(workflow.ActionType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported workflow action type: {workflow.ActionType}");

        if (workflow.ActionType == WorkflowActionTypes.EmailReport)
        {
            if (workflow.Enabled && !workflow.IsDeleted && string.IsNullOrWhiteSpace(workflow.CustomPrompt))
                throw new InvalidOperationException("Prompt is required for Email Report workflows.");
        }
    }
}
