using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Data;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class ClickHouseWorkflowStore(SentinelClickHouseContext db, ILogger<ClickHouseWorkflowStore> logger) : IWorkflowStore
{
    public async Task<List<WorkflowDefinition>> GetAllAsync() =>
        await db.Workflows
            .FromSqlRaw("SELECT * FROM sentinel.workflows FINAL WHERE is_deleted = 0 ORDER BY updated_at DESC")
            .ToListAsync();

    public async Task<List<WorkflowDefinition>> GetEnabledAsync() =>
        await db.Workflows
            .FromSqlRaw("SELECT * FROM sentinel.workflows FINAL WHERE is_deleted = 0 AND enabled = 1 ORDER BY updated_at DESC")
            .ToListAsync();

    public async Task<WorkflowDefinition?> GetByIdAsync(string id) =>
        await db.Workflows
            .FromSqlRaw($"SELECT * FROM sentinel.workflows FINAL WHERE id = '{Esc(id)}' AND is_deleted = 0")
            .FirstOrDefaultAsync();

    public async Task UpsertAsync(WorkflowDefinition workflow)
    {
        NormalizeAndValidate(workflow);

        if (string.IsNullOrWhiteSpace(workflow.Id))
            workflow.Id = Guid.NewGuid().ToString("N");

        workflow.UpdatedAt = DateTime.UtcNow;
        if (workflow.CreatedAt == default)
            workflow.CreatedAt = DateTime.UtcNow;

        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO sentinel.workflows
                (id, name, description, action_type, cron_expression, enabled,
                 target_database, sql_query, email_subject, email_recipients,
                 custom_prompt, system_prompt, is_deleted, created_at, updated_at, created_by)
            VALUES
                ('{Esc(workflow.Id)}', '{Esc(workflow.Name)}', '{Esc(workflow.Description)}',
                 '{Esc(workflow.ActionType)}', '{Esc(workflow.CronExpression)}', {(workflow.Enabled ? 1 : 0)},
                 '{Esc(workflow.TargetDatabase)}', '{Esc(workflow.SqlQuery)}', '{Esc(workflow.EmailSubject)}',
                 '{Esc(workflow.EmailRecipients)}', '{Esc(workflow.CustomPrompt)}', '{Esc(workflow.SystemPrompt)}',
                 {(workflow.IsDeleted ? 1 : 0)},
                 '{workflow.CreatedAt:yyyy-MM-dd HH:mm:ss}', '{workflow.UpdatedAt:yyyy-MM-dd HH:mm:ss}', '{Esc(workflow.CreatedBy)}')
            """);
    }

    public async Task DeleteAsync(string id)
    {
        var existing = await GetByIdAsync(id);
        if (existing is null)
            return;

        existing.Enabled = false;
        existing.IsDeleted = true;
        await UpsertAsync(existing);
    }

    private static string Esc(string? s) => (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'");

    public async Task EnsureTableAsync()
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS sentinel.workflows (
                    id String,
                    name String,
                    description String DEFAULT '',
                    action_type String DEFAULT 'sql_email_report',
                    cron_expression String DEFAULT '0 * * * *',
                    enabled UInt8 DEFAULT 1,
                    target_database String DEFAULT '',
                    sql_query String DEFAULT '',
                    email_subject String DEFAULT '',
                    email_recipients String DEFAULT '',
                    custom_prompt String DEFAULT '',
                    system_prompt String DEFAULT '',
                    is_deleted UInt8 DEFAULT 0,
                    created_at DateTime DEFAULT now(),
                    updated_at DateTime DEFAULT now(),
                    created_by String DEFAULT 'system'
                ) ENGINE = ReplacingMergeTree(updated_at)
                ORDER BY id
                """);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ensure workflows table");
            throw;
        }
    }

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
                SqlQuery = workflow.SqlQuery,
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
        workflow.ActionType = (workflow.ActionType ?? "").Trim().ToLowerInvariant();

        if (!WorkflowActionTypes.All.Contains(workflow.ActionType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported workflow action type: {workflow.ActionType}");

        if (workflow.ActionType == WorkflowActionTypes.SqlEmailReport)
        {
            // Prompt-only policy for email report workflows.
            workflow.SqlQuery = "";
            if (workflow.Enabled && !workflow.IsDeleted && string.IsNullOrWhiteSpace(workflow.CustomPrompt))
                throw new InvalidOperationException("Prompt is required for SQL Email Report workflows.");
        }
    }
}
