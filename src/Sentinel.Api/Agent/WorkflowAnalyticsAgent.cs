using Sentinel.Admin.Models;

namespace Sentinel.Agent;

/// <summary>
/// Facade over <see cref="AnalyticsAgentCore"/> for autonomous, scheduled workflow runs:
/// no user prompts or charts, the agent is expected to deliver a report, and each tool call
/// can be persisted for the run audit trail.
/// </summary>
public class WorkflowAnalyticsAgent(AnalyticsAgentCore core, IConfiguration config)
{
    public Task<AnalyticsResponse> RunAsync(
        string prompt,
        string database,
        IEnumerable<AgentMemory>? memories = null,
        Func<AgentToolCall, Task>? onToolCall = null,
        string? model = null,
        string? reasoningEffort = null,
        IReadOnlyList<string>? additionalDatabases = null,
        CancellationToken cancellationToken = default)
    {
        // Both budgets are tunable without a rebuild, but the defaults now match what a report
        // actually needs. The old 4096-token cap could not hold a multi-section report inside a
        // single send_report argument, and 15 iterations ran out while the agent was still
        // querying — in both cases the run ended with no report and nothing to fall back on.
        var maxOutputTokens = config.GetValue("Analytics:WorkflowMaxOutputTokens", 16000);
        var maxIterations = config.GetValue("Analytics:WorkflowMaxIterations", 40);

        return core.AskAsync(
            prompt,
            database,
            AgentProfile.Workflow(maxOutputTokens, model, reasoningEffort, maxIterations),
            history: null,
            onEvent: null,
            onToolCall: onToolCall,
            memories: memories,
            additionalDatabases: additionalDatabases,
            cancellationToken: cancellationToken);
    }
}