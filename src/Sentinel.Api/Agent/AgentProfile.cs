namespace Sentinel.Agent;

/// <summary>
/// Describes how the shared <see cref="AnalyticsAgentCore"/> should behave for a given caller.
/// The only real divergence between chat and workflow runs is whether the agent is interactive
/// (chat: streaming, can ask the user, can draw charts) vs autonomous (workflow: must send a
/// report, no user prompts), plus the output token budget.
/// </summary>
public sealed record AgentProfile(bool Interactive, int MaxOutputTokens)
{
    /// <summary>Interactive chat: streaming answers, ask_user + emit_chart available.</summary>
    public static AgentProfile Chat(int maxOutputTokens = 4096) =>
        new(Interactive: true, MaxOutputTokens: maxOutputTokens);

    /// <summary>Autonomous workflow run: no user prompts/charts, send_report expected.</summary>
    public static AgentProfile Workflow(int maxOutputTokens = 4096) =>
        new(Interactive: false, MaxOutputTokens: maxOutputTokens);
}