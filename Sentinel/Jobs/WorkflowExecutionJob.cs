using Hangfire;
using Sentinel.Agent;
using Sentinel.Admin;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;
using Sentinel.Infrastructure;

namespace Sentinel.Jobs;

[Queue("default")]
public class WorkflowExecutionJob(
    IWorkflowStore workflowStore,
    IRunLogStore runLogStore,
    IActiveRunTracker runTracker,
    AnalyticsAgent analyticsAgent,
    IAgentMemoryStore agentMemoryStore,
    EmailClient emailClient,
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
            var runId = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var triggeredBy = $"workflow:{workflow.Id}";
            await runTracker.MarkQueuedAsync(runId, triggeredBy, DateTime.UtcNow);

            backgroundJobs.Enqueue<SentinelJob>(job =>
                job.RunAsync(new FraudAgentRunRequest
                {
                    TriggeredBy = triggeredBy,
                    RunId = runId,
                    Database = workflow.TargetDatabase,
                    CustomPrompt = workflow.CustomPrompt,
                    WorkflowId = workflow.Id
                }));
            logger.LogInformation("Workflow {WorkflowId} enqueued fraud run {RunId}", workflow.Id, runId);
            return;
        }

        throw new InvalidOperationException($"Unsupported workflow action type: {workflow.ActionType}");
    }

    private async Task ExecuteEmailReportAsync(WorkflowDefinition workflow)
    {
        var runId = Guid.NewGuid().ToString("N")[..16];
        var startedAt = DateTime.UtcNow;
        var triggeredBy = $"workflow:{workflow.Id}";
        var database = string.IsNullOrWhiteSpace(workflow.TargetDatabase)
            ? "lipila_blaze"
            : workflow.TargetDatabase;

        if (string.IsNullOrWhiteSpace(workflow.CustomPrompt))
            throw new InvalidOperationException($"Workflow {workflow.Id} has no prompt configured.");

        await runTracker.MarkRunningAsync(runId, triggeredBy, startedAt);

        var recipients = ParseRecipients(workflow.EmailRecipients);
        var recipientHint = recipients is { Count: > 0 }
            ? $"\n\nSend the report to: {string.Join(", ", recipients)}"
            : "";

        // Be very explicit — the model must call send_report; we name the tool and
        // remind it that skipping it means the workflow has failed its purpose.
        var prompt = workflow.CustomPrompt + recipientHint + """


            IMPORTANT: You MUST call the send_report tool to deliver your findings by email.
            Do NOT finish without calling send_report — if you do not call it the workflow will be considered failed.
            Use template="insights", include an executive summary, key metrics, and recommendations in the body.
            """;

        AnalyticsResponse? result = null;
        var status = "error";
        var maxIteration = 0;
        try
        {
            var memories = await agentMemoryStore.GetEnabledAsync(database);
            result = await analyticsAgent.AskAsync(prompt, database, mode: "autonomous", memories: memories,
                onToolCall: async tc =>
                {
                    if (tc.Iteration > maxIteration) maxIteration = tc.Iteration;
                    await runLogStore.LogToolCallAsync(new RunLog
                    {
                        RunId = runId,
                        Iteration = (ushort)tc.Iteration,
                        ToolName = tc.ToolName,
                        Args = tc.Args,
                        Result = tc.Result.Length > 10_000 ? tc.Result[..10_000] : tc.Result,
                        StartedAt = tc.StartedAt,
                        DurationMs = (uint)tc.DurationMs
                    });
                });
            status = result.Success ? "completed" : "error";

            if (!result.Success)
                throw new InvalidOperationException(
                    $"Workflow {workflow.Id} analysis failed: {result.Error ?? "unknown error"}");

            // ── Fallback: agent finished but never called send_report ──────────────
            if (!result.ReportSent)
            {
                logger.LogWarning(
                    "Workflow {WorkflowId} run {RunId}: agent did not call send_report — sending fallback email",
                    workflow.Id, runId);

                var fallbackSubject = !string.IsNullOrWhiteSpace(workflow.EmailSubject)
                    ? workflow.EmailSubject
                    : $"{workflow.Name} — Scheduled Report";

                var fallbackBody = !string.IsNullOrWhiteSpace(result.Explanation)
                    ? result.Explanation
                    : "The scheduled report ran but produced no explanation. Please review the workflow configuration.";

                await emailClient.SendAsync(fallbackSubject, fallbackBody, "watching",
                    recipients?.ToList());

                result.ReportSent   = true;
                result.EmailSubject = fallbackSubject;
                result.EmailBody    = fallbackBody;
            }
            // ─────────────────────────────────────────────────────────────────────

            logger.LogInformation(
                "Workflow {WorkflowId} run {RunId} completed. ReportSent={Sent} Subject={Subject}",
                workflow.Id, runId, result.ReportSent, result.EmailSubject);
        }
        finally
        {
            await runLogStore.SaveSummaryAsync(new RunSummary
            {
                RunId        = runId,
                StartedAt    = startedAt,
                FinishedAt   = DateTime.UtcNow,
                Iterations   = (ushort)maxIteration,
                InputTokens  = (uint)(result?.InputTokens  ?? 0),
                OutputTokens = (uint)(result?.OutputTokens ?? 0),
                CasesCreated    = 0,
                CasesResolved   = 0,
                AlertsSent   = (ushort)((result?.ReportSent ?? false) ? 1 : 0),
                Status       = status,
                TriggeredBy  = triggeredBy,
                // Persist the generated email content so it can be reviewed later
                EmailSubject = result?.EmailSubject,
                EmailBody    = result?.EmailBody
            });

            if (status == "completed")
                await runTracker.MarkCompletedAsync(runId);
            else
                await runTracker.MarkFailedAsync(runId);
        }
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