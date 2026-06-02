namespace Sentinel.Agent;

public record FraudAgentRunRequest
{
    public string TriggeredBy { get; init; } = "scheduler";
    public string? RunId { get; init; }
    public string? Database { get; init; }
    public string? CustomPrompt { get; init; }
    public string? WorkflowId { get; init; }
}
