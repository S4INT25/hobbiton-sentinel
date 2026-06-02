using Hangfire;
using Sentinel.Agent;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;

namespace Sentinel.Jobs;

[Queue("default")]
public class WorkflowExecutionJob(
    IWorkflowStore workflowStore,
    IRunLogStore runLogStore,
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
        var runId = Guid.NewGuid().ToString("N")[..16];
        var startedAt = DateTime.UtcNow;
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

        // Save run summary so it appears in the UI
        await runLogStore.SaveSummaryAsync(new RunSummary
        {
            RunId = runId,
            StartedAt = startedAt,
            FinishedAt = DateTime.UtcNow,
            Iterations = 0,
            InputTokens = (uint)result.InputTokens,
            OutputTokens = (uint)result.OutputTokens,
            CasesCreated = 0,
            CasesResolved = 0,
            AlertsSent = (ushort)(result.ReportSent ? 1 : 0),
            Status = result.Success ? "completed" : "error",
            TriggeredBy = $"workflow:{workflow.Id}"
        });

        if (!result.Success)
            throw new InvalidOperationException(
                $"Workflow {workflow.Id} analysis failed: {result.Error ?? "unknown error"}");

        logger.LogInformation("Workflow {WorkflowId} run {RunId} completed. ReportSent={Sent}",
            workflow.Id, runId, result.ReportSent);
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