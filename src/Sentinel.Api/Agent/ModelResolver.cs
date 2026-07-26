using OpenAI;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;

namespace Sentinel.Agent;

public record ResolvedModel(OpenAIClient Client, string ModelId);

/// <summary>
/// Resolves which provider client and model an agent run should use. A requested model
/// is only honoured when it is registered and enabled; otherwise the admin-marked default
/// (or, failing that, the configured fallback) is used.
/// </summary>
public class ModelResolver(
    ILlmModelStore modelStore,
    IProviderStore providerStore,
    LlmClientFactory clientFactory,
    IConfiguration config)
{
    public async Task<ResolvedModel> ResolveAsync(string? requested)
    {
        var enabled = await modelStore.GetEnabledAsync();
        var enabledProviders = await providerStore.GetEnabledAsync();

        LlmModel? resolvedModel = null;

        if (!string.IsNullOrWhiteSpace(requested))
        {
            resolvedModel = enabled.FirstOrDefault(m =>
                string.Equals(m.ModelId, requested.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        resolvedModel ??= enabled.FirstOrDefault(m => m.IsDefault) ?? enabled.FirstOrDefault();

        var modelId = resolvedModel?.ModelId
                      ?? config["OpenRouter:DefaultModel"]
                      ?? "deepseek/deepseek-v4-flash";

        var provider = resolvedModel is { ProviderId: > 0 }
            ? enabledProviders.FirstOrDefault(p => p.Id == resolvedModel.ProviderId)
            : null;
        provider ??= enabledProviders.FirstOrDefault(p => p.IsDefault)
                    ?? enabledProviders.FirstOrDefault(p => p.Slug == "openrouter")
                    ?? enabledProviders.FirstOrDefault();

        var client = provider is not null
            ? clientFactory.GetOrCreate(provider.Endpoint, provider.ApiKey)
            : clientFactory.GetOrCreate(
                config["OpenRouter:Endpoint"] ?? "https://openrouter.ai/api/v1",
                config["OpenRouter:ApiKey"] ?? "");

        return new ResolvedModel(client, modelId);
    }
}
