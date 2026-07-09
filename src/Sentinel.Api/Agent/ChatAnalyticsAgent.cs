using System.Diagnostics.CodeAnalysis;
using Sentinel.Admin;
using Sentinel.Admin.Models;

namespace Sentinel.Agent;

/// <summary>
/// Facade over <see cref="AnalyticsAgentCore"/> for the interactive chat experience:
/// streaming responses, follow-up questions (ask_user) and inline charts (emit_chart).
/// </summary>
public class ChatAnalyticsAgent(AnalyticsAgentCore core)
{
    [Experimental("OPENAI001")]
    public Task<AnalyticsResponse> AskAsync(
        string prompt,
        string database = "lipila_blaze",
        List<ChatEntry>? history = null,
        Func<AnalyticsStreamEvent, Task>? onEvent = null,
        IEnumerable<AgentMemory>? memories = null,
        CancellationToken cancellationToken = default)
        => core.AskAsync(
            prompt,
            database,
            AgentProfile.Chat(),
            history,
            onEvent,
            onToolCall: null,
            memories,
            cancellationToken);
}