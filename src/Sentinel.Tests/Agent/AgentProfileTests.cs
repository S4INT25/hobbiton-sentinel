using Sentinel.Agent;

namespace Sentinel.Tests.Agent;

/// <summary>
/// A scheduled report has to explore several databases and then fit a whole multi-section
/// markdown report inside one send_report argument. On chat-sized budgets the run either spent
/// its iterations mid-investigation or had the tool call truncated, and in both cases ended with
/// no report and no text to fall back on. These guard against quietly shrinking them again.
/// </summary>
public class AgentProfileTests
{
    [Fact]
    public void Workflow_runs_get_a_bigger_output_budget_than_chat()
    {
        Assert.True(AgentProfile.Workflow().MaxOutputTokens > AgentProfile.Chat().MaxOutputTokens);
    }

    [Fact]
    public void Workflow_output_budget_can_hold_a_full_report()
    {
        // A multi-section digest body runs to several thousand tokens on its own, before the
        // rest of the tool-call JSON. 4096 — the old value — could not hold one.
        Assert.True(AgentProfile.Workflow().MaxOutputTokens >= 12000,
            "Workflow output budget is too small to emit a full report in one send_report call.");
    }

    [Fact]
    public void Workflow_runs_get_more_iterations_than_chat()
    {
        Assert.True(AgentProfile.Workflow().MaxIterations > AgentProfile.Chat().MaxIterations);
        Assert.True(AgentProfile.Workflow().MaxIterations >= 30,
            "A cross-database digest needs room to query every platform before it can write.");
    }

    [Fact]
    public void Workflow_is_autonomous_and_chat_is_interactive()
    {
        // Interactive gates ask_user/emit_chart and the send_report enforcement path.
        Assert.False(AgentProfile.Workflow().Interactive);
        Assert.True(AgentProfile.Chat().Interactive);
    }

    [Fact]
    public void Explicit_arguments_override_the_defaults()
    {
        var p = AgentProfile.Workflow(maxOutputTokens: 999, maxIterations: 7);
        Assert.Equal(999, p.MaxOutputTokens);
        Assert.Equal(7, p.MaxIterations);
    }
}
