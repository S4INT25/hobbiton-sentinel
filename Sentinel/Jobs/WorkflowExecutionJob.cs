using Hangfire;
using Sentinel.Agent;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;
using Sentinel.Infrastructure;

namespace Sentinel.Jobs;

[Queue("default")]
public class WorkflowExecutionJob(
    IWorkflowStore workflowStore,
    AnalyticsAgent analyticsAgent,
    IBackgroundJobClient backgroundJobs,
    ILogger<WorkflowExecutionJob> logger)
{
    public async Task ExecuteAsync(string workflowId)
    {
        var workflow = await workflowStore.GetByIdAsync(workflowId);
        if (workflow is null)
        {
            logger.LogWarning("Workflow {WorkflowId} not found; skipping execution", workflowId);
            return;
        }

        if (!workflow.Enabled || workflow.IsDeleted)
        {
            logger.LogInformation("Workflow {WorkflowId} is disabled/deleted; skipping execution", workflowId);
            return;
        }

        var actionType = WorkflowActionTypes.Normalize(workflow.ActionType);

        if (actionType == WorkflowActionTypes.EmailReport)
        {
            await ExecuteEmailReportAsync(workflow);
            return;
        }

        if (actionType == WorkflowActionTypes.FraudRun)
        {
            backgroundJobs.Enqueue<SentinelJob>(job =>
                job.RunAsync(new FraudAgentRunRequest
                {
                    TriggeredBy = $"workflow:{workflow.Id}",
                    Database = workflow.TargetDatabase,
                    CustomPrompt = workflow.CustomPrompt,
                    WorkflowId = workflow.Id
                }));
            logger.LogInformation("Workflow {WorkflowId} enqueued fraud run", workflow.Id);
            return;
        }

        throw new InvalidOperationException($"Unsupported workflow action type: {workflow.ActionType}");
    }

    private async Task ExecuteEmailReportAsync(WorkflowDefinition workflow)
    {
        var database = string.IsNullOrWhiteSpace(workflow.TargetDatabase)
            ? "lipila_blaze"
            : workflow.TargetDatabase;

        if (string.IsNullOrWhiteSpace(workflow.CustomPrompt))
            throw new InvalidOperationException($"Workflow {workflow.Id} has no prompt configured.");

        // Build prompt with recipient info so the agent uses send_report correctly
        var recipients = ParseRecipients(workflow.EmailRecipients);
        var recipientHint = recipients is { Count: > 0 }
            ? $"\n\nSend the report to: {string.Join(", ", recipients)}"
            : "";

        var prompt = workflow.CustomPrompt + recipientHint +
            "\n\nYou MUST call the send_report tool with your findings.";

        var result = await analyticsAgent.AskAsync(prompt, database, mode: "autonomous");

        if (!result.Success)
            throw new InvalidOperationException(
                $"Workflow {workflow.Id} analysis failed: {result.Error ?? "unknown error"}");

        logger.LogInformation("Workflow {WorkflowId} completed. ReportSent={Sent}", workflow.Id, result.ReportSent);
    }

    private static IReadOnlyList<string>? ParseRecipients(string recipients)
    {
        if (string.IsNullOrWhiteSpace(recipients))
            return null;

        var parsed = recipients
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return parsed.Count == 0 ? null : parsed;
    }
}