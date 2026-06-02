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

        var actionType = workflow.ActionType.Trim().ToLowerInvariant();

        if (actionType == WorkflowActionTypes.SqlEmailReport)
        {
            await ExecuteSqlEmailReportAsync(workflow);
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

    private async Task ExecuteSqlEmailReportAsync(WorkflowDefinition workflow)
    {
        var database = string.IsNullOrWhiteSpace(workflow.TargetDatabase)
            ? "lipila_blaze"
            : workflow.TargetDatabase;

        if (string.IsNullOrWhiteSpace(workflow.CustomPrompt))
            throw new InvalidOperationException($"Workflow {workflow.Id} has no prompt configured.");

        var result = await analyticsAgent.AskAsync(workflow.CustomPrompt, database);
        if (!result.Success)
            throw new InvalidOperationException($"Workflow {workflow.Id} analysis failed: {result.Error ?? "unknown error"}");

        var body = BuildAgentReportBody(workflow, database, result);

        var subject = string.IsNullOrWhiteSpace(workflow.EmailSubject)
            ? $"Workflow Report: {workflow.Name}"
            : workflow.EmailSubject;

        await emailClient.SendAsync(subject, body, "watching", ParseRecipients(workflow.EmailRecipients));
    }

    private static string BuildAgentReportBody(WorkflowDefinition workflow, string database, AnalyticsResponse result)
    {
        var firstResult = result.Results.FirstOrDefault();
        var sql = firstResult?.Sql ?? result.Sql ?? "(agent returned no SQL)";
        var rows = firstResult?.Rows ?? result.Rows;
        var columns = firstResult?.Columns ?? result.Columns;
        var rowCount = firstResult?.RowCount ?? result.RowCount;

        var lines = new List<string>
        {
            $"# {workflow.Name}",
            "",
            workflow.Description,
            "",
            $"**Database:** `{database}`",
            $"**Rows:** {rowCount}",
            ""
        };

        if (!string.IsNullOrWhiteSpace(result.Explanation))
        {
            lines.Add("## Summary");
            lines.Add(result.Explanation!);
            lines.Add("");
        }

        lines.Add("## Generated SQL");
        lines.Add("```sql");
        lines.Add(sql);
        lines.Add("```");
        lines.Add("");

        if (columns.Count > 0 && rows.Count > 0)
        {
            lines.Add("## Sample Rows");
            lines.Add("| " + string.Join(" | ", columns) + " |");
            lines.Add("|" + string.Join("|", columns.Select(_ => "---")) + "|");

            foreach (var row in rows.Take(20))
            {
                var values = columns
                    .Select(c => row.TryGetValue(c, out var v) ? v.Replace("|", "\\|") : "")
                    .ToList();
                lines.Add("| " + string.Join(" | ", values) + " |");
            }
        }

        return Truncate(string.Join('\n', lines), 15000);
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

    private static string Truncate(string value, int limit) =>
        value.Length <= limit ? value : value[..limit] + "\n... (truncated)";
}
