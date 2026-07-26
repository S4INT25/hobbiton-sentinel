using Sentinel.Admin.Stores;

namespace Sentinel.Agent;

/// <summary>
/// Resolves which OpenRouter model an agent run should use. A requested model is only
/// honoured when it is registered and enabled; otherwise the admin-marked default
/// (or, failing that, the configured fallback) is used — so chat/workflow selections
/// can never point at an arbitrary or disabled model.
/// </summary>
public class ModelResolver(ILlmModelStore store, IConfiguration config)
{
    public async Task<string> ResolveAsync(string? requested)
    {
        var enabled = await store.GetEnabledAsync();

        if (!string.IsNullOrWhiteSpace(requested))
        {
            var match = enabled.FirstOrDefault(m =>
                string.Equals(m.ModelId, requested.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match.ModelId;
        }

        var fallback = enabled.FirstOrDefault(m => m.IsDefault) ?? enabled.FirstOrDefault();
        return fallback?.ModelId
               ?? config["OpenRouter:DefaultModel"]
               ?? "anthropic/claude-sonnet-4.5";
    }
}
