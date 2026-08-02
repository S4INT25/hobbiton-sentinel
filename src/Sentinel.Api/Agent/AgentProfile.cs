namespace Sentinel.Agent;

/// <summary>
/// Describes how the shared <see cref="AnalyticsAgentCore"/> should behave for a given caller.
/// The only real divergence between chat and workflow runs is whether the agent is interactive
/// (chat: streaming, can ask the user, can draw charts) vs autonomous (workflow: must send a
/// report, no user prompts), plus the output token budget, model and reasoning effort.
/// </summary>
public sealed record AgentProfile(
    bool Interactive,
    int MaxOutputTokens,
    string? Model = null,
    string? ReasoningEffort = null,
    int MaxIterations = 15)
{
    /// <summary>Interactive chat: streaming answers, ask_user + emit_chart available.</summary>
    public static AgentProfile Chat(int maxOutputTokens = 4096, string? model = null, string? reasoningEffort = null) =>
        new(Interactive: true, MaxOutputTokens: maxOutputTokens, Model: model, ReasoningEffort: reasoningEffort);

    /// <summary>
    /// Autonomous workflow run: no user prompts/charts, send_report expected.
    ///
    /// Both budgets are far larger than chat's. A scheduled report explores several databases
    /// before it can write anything, and then has to fit an entire multi-section markdown report
    /// inside one send_report argument. On the chat budget the run either exhausted its
    /// iterations mid-investigation or had the tool call truncated — either way it ended with no
    /// report and no text to fall back on.
    /// </summary>
    public static AgentProfile Workflow(
        int maxOutputTokens = 16000,
        string? model = null,
        string? reasoningEffort = null,
        int maxIterations = 40) =>
        new(Interactive: false, MaxOutputTokens: maxOutputTokens, Model: model,
            ReasoningEffort: reasoningEffort, MaxIterations: maxIterations);
}
