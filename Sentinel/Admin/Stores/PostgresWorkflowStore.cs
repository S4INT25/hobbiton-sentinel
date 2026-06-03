using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class PostgresWorkflowStore(
    IDbContextFactory<SentinelDbContext> dbFactory,
    ILogger<PostgresWorkflowStore> logger) : IWorkflowStore
{
    public async Task<List<WorkflowDefinition>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Workflows
            .AsNoTracking()
            .Where(w => !w.IsDeleted)
            .OrderByDescending(w => w.UpdatedAt)
            .ToListAsync();
    }

    public async Task<List<WorkflowDefinition>> GetEnabledAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Workflows
            .AsNoTracking()
            .Where(w => !w.IsDeleted && w.Enabled)
            .OrderByDescending(w => w.UpdatedAt)
            .ToListAsync();
    }

    public async Task<WorkflowDefinition?> GetByIdAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Workflows
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id && !w.IsDeleted);
    }

    public async Task UpsertAsync(WorkflowDefinition workflow)
    {
        NormalizeAndValidate(workflow);

        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTimeOffset.UtcNow;
        workflow.UpdatedAt = now;

        var existing = await db.Workflows.FirstOrDefaultAsync(w => w.Id == workflow.Id);
        if (existing is null)
        {
            if (string.IsNullOrWhiteSpace(workflow.Id))
                workflow.Id = Guid.NewGuid().ToString("N");
            workflow.CreatedAt = now;
            db.Workflows.Add(workflow);
        }
        else
        {
            existing.Name = workflow.Name;
            existing.Description = workflow.Description;
            existing.ActionType = workflow.ActionType;
            existing.CronExpression = workflow.CronExpression;
            existing.Enabled = workflow.Enabled;
            existing.TargetDatabase = workflow.TargetDatabase;
            existing.EmailSubject = workflow.EmailSubject;
            existing.EmailRecipients = workflow.EmailRecipients;
            existing.CustomPrompt = workflow.CustomPrompt;
            existing.SystemPrompt = workflow.SystemPrompt;
            existing.IsDeleted = workflow.IsDeleted;
            existing.UpdatedAt = now;
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var workflow = await db.Workflows.FirstOrDefaultAsync(w => w.Id == id);
        if (workflow is null) return;
        workflow.IsDeleted = true;
        workflow.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task SeedDefaultsAsync()
    {
        var existing = await GetAllAsync();
        var now = DateTimeOffset.UtcNow;

        foreach (var workflow in WorkflowDefaults.All)
        {
            var exists = existing.Any(w =>
                string.Equals(w.Id, workflow.Id, StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(w.ActionType, workflow.ActionType, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(w.Name, workflow.Name, StringComparison.OrdinalIgnoreCase)));

            if (exists) continue;

            await UpsertAsync(new WorkflowDefinition
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
            });
            logger.LogInformation("Seeded default workflow {WorkflowId} ({WorkflowName})", workflow.Id, workflow.Name);
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
