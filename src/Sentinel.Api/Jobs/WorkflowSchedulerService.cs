using System.Diagnostics.CodeAnalysis;
using Hangfire;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;

namespace Sentinel.Jobs;

public class WorkflowSchedulerService(
    IRecurringJobManager jobs,
    IServiceScopeFactory scopeFactory,
    ILogger<WorkflowSchedulerService> logger)
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    [Experimental("OPENAI001")]
    public async Task RefreshSchedulesAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var workflowStore = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
            var workflows = await workflowStore.GetAllAsync();

            foreach (var workflow in workflows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var recurringId = RecurringId(workflow.Id);

                if (!workflow.Enabled || workflow.IsDeleted)
                {
                    jobs.RemoveIfExists(recurringId);
                    continue;
                }

                if (!WorkflowActionTypes.All.Contains(workflow.ActionType, StringComparer.OrdinalIgnoreCase))
                {
                    logger.LogWarning("Workflow {WorkflowId} has unsupported action type: {ActionType}",
                        workflow.Id, workflow.ActionType);
                    jobs.RemoveIfExists(recurringId);
                    continue;
                }

                if (!IsValidCron(workflow.CronExpression))
                {
                    logger.LogWarning("Workflow {WorkflowId} has invalid cron expression: {Cron}",
                        workflow.Id, workflow.CronExpression);
                    jobs.RemoveIfExists(recurringId);
                    continue;
                }

                var workflowTimeZone = WorkflowTimeZones.ResolveOrDefault(workflow.TimeZoneId);
                jobs.AddOrUpdate<WorkflowExecutionJob>(
                    recurringJobId: recurringId,
                    methodCall: job => job.ExecuteAsync(workflow.Id),
                    cronExpression: workflow.CronExpression,
                    queue: "default",
                    options: new RecurringJobOptions
                    {
                        TimeZone = workflowTimeZone
                    });
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void RemoveWorkflowSchedule(string workflowId) =>
        jobs.RemoveIfExists(RecurringId(workflowId));

    public static bool IsValidCron(string cron)
    {
        if (string.IsNullOrWhiteSpace(cron))
            return false;

        var parts = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 5)
            return false;

        return parts.All(static part => part.All(c =>
            char.IsDigit(c) || c is '*' or '/' or ',' or '-' or '?'));
    }

    /// <summary>
    /// True when the cron targets a single specific date/time (fixed minute, hour, day-of-month
    /// and month, with any day-of-week). Cron has no year field, so such a schedule would
    /// otherwise recur every year — we treat it as a "run once" schedule instead.
    /// </summary>
    public static bool IsOneShotCron(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron))
            return false;

        var parts = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 5 &&
               int.TryParse(parts[0], out _) && // minute
               int.TryParse(parts[1], out _) && // hour
               int.TryParse(parts[2], out _) && // day of month
               int.TryParse(parts[3], out _) && // month
               parts[4] == "*"; // any day of week
    }

    private static string RecurringId(string workflowId) => $"workflow:{workflowId}";
}