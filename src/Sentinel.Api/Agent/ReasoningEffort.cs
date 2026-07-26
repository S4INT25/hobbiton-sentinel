using OpenAI.Chat;

namespace Sentinel.Agent;

/// <summary>
/// Reasoning effort handling for OpenRouter. Sends the OpenRouter-normalized
/// <c>reasoning</c> parameter, which OpenRouter translates per-provider (thinking for
/// Anthropic, reasoning_effort for OpenAI, …) and ignores for non-reasoning models —
/// so it is safe to send for any model. Null/empty = model default behaviour.
/// </summary>
public static class ReasoningEffort
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";

    /// <summary>Returns the normalized effort ("low"|"medium"|"high") or null when unset/invalid.</summary>
    public static string? Normalize(string? effort) =>
        effort?.Trim().ToLowerInvariant() switch
        {
            Low or Medium or High => effort!.Trim().ToLowerInvariant(),
            _ => null
        };

    /// <summary>Applies the reasoning parameter to the request when an effort is set.</summary>
    public static void Apply(ChatCompletionOptions options, string? effort)
    {
        var normalized = Normalize(effort);
        if (normalized is null) return;
#pragma warning disable SCME0001
        options.Patch.Set("$.reasoning"u8, BinaryData.FromObjectAsJson(new { effort = normalized }));
#pragma warning restore SCME0001
    }
}
