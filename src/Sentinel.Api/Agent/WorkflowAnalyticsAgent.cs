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
        CancellationToken cancellationToken = default)
    {
        // Reports can be long; allow tuning the budget without a code change (default keeps
        // the historical 4096 cap so this is a no-op until configured).
        var maxOutputTokens = config.GetValue("Analytics:WorkflowMaxOutputTokens", 4096);

        return core.AskAsync(
            prompt,
            database,
            AgentProfile.Workflow(maxOutputTokens),
            history: null,
            onEvent: null,
            onToolCall: onToolCall,
            memories: memories,
            cancellationToken: cancellationToken);
    }
}