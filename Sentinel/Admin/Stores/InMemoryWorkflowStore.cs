using System.Collections.Concurrent;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class InMemoryWorkflowStore : IWorkflowStore
{
    private readonly ConcurrentDictionary<string, WorkflowDefinition> _workflows = new();

    public Task<List<WorkflowDefinition>> GetAllAsync() =>
        Task.FromResult(_workflows.Values
            .Where(w => !w.IsDeleted)
            .OrderBy(w => w.Name)
            .ToList());

    public Task<List<WorkflowDefinition>> GetEnabledAsync() =>
        Task.FromResult(_workflows.Values
            .Where(w => !w.IsDeleted && w.Enabled)
            .OrderBy(w => w.Name)
            .ToList());

    public Task<WorkflowDefinition?> GetByIdAsync(string id)
    {
        if (!_workflows.TryGetValue(id, out var workflow) || workflow.IsDeleted)
            return Task.FromResult<WorkflowDefinition?>(null);
        return Task.FromResult<WorkflowDefinition?>(workflow);
    }

    public Task UpsertAsync(WorkflowDefinition workflow)
    {
        NormalizeAndValidate(workflow);

        if (string.IsNullOrWhiteSpace(workflow.Id))
            workflow.Id = Guid.NewGuid().ToString("N");

        var now = DateTime.UtcNow;
        if (workflow.CreatedAt == default)
            workflow.CreatedAt = now;
        workflow.UpdatedAt = now;
        _workflows[workflow.Id] = workflow;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id)
    {
        if (_workflows.TryGetValue(id, out var workflow))
        {
            workflow.Enabled = false;
            workflow.IsDeleted = true;
            workflow.UpdatedAt = DateTime.UtcNow;
            _workflows[id] = workflow;
        }

        return Task.CompletedTask;
    }

    public Task SeedDefaultsAsync()
    {
        foreach (var workflow in WorkflowDefaults.All)
        {
            var exists = _workflows.Values.Any(w =>
                !w.IsDeleted &&
                (string.Equals(w.Id, workflow.Id, StringComparison.OrdinalIgnoreCase) ||
                 (string.Equals(w.ActionType, workflow.ActionType, StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(w.Name, workflow.Name, StringComparison.OrdinalIgnoreCase))));

            if (exists)
                continue;

            var seeded = new WorkflowDefinition
            {
                Id = workflow.Id,
                Name = workflow.Name,
                Description = workflow.Description,
                ActionType = workflow.ActionType,
                CronExpression = workflow.CronExpression,
                TimeZoneId = workflow.TimeZoneId,
                Enabled = workflow.Enabled,
                TargetDatabase = workflow.TargetDatabase,
                EmailSubject = workflow.EmailSubject,
                EmailRecipients = workflow.EmailRecipients,
                CustomPrompt = workflow.CustomPrompt,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = workflow.CreatedBy
            };

            _workflows[seeded.Id] = seeded;
        }

        return Task.CompletedTask;
    }

    private static void NormalizeAndValidate(WorkflowDefinition workflow)
    {
        workflow.ActionType = WorkflowActionTypes.Normalize(workflow.ActionType);
        workflow.TimeZoneId = WorkflowTimeZones.NormalizeOrDefaultId(workflow.TimeZoneId);

        if (!WorkflowActionTypes.All.Contains(workflow.ActionType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported workflow action type: {workflow.ActionType}");

        if (workflow.ActionType == WorkflowActionTypes.EmailReport)
        {
            if (workflow.Enabled && !workflow.IsDeleted && string.IsNullOrWhiteSpace(workflow.CustomPrompt))
                throw new InvalidOperationException("Prompt is required for Email Report workflows.");
        }
    }
}
