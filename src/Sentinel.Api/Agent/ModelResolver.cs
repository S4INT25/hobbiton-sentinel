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
            ? clientFactory.GetOrCreate(provider.Endpoint, ResolveApiKey(provider))
            : clientFactory.GetOrCreate(
                config["OpenRouter:Endpoint"] ?? "https://openrouter.ai/api/v1",
                config["OpenRouter:ApiKey"] ?? "");

        return new ResolvedModel(client, modelId);
    }

    /// <summary>
    /// The provider's API key: the stored value if an admin has entered one, otherwise
    /// configuration (<c>Providers:{slug}:ApiKey</c>, i.e. env <c>Providers__{slug}__ApiKey</c>).
    ///
    /// The config fallback exists because a key that lives only in a database row disappears with
    /// the row — a redeploy that recreates the volume silently leaves every agent unauthenticated.
    /// Supplying it through the environment means credentials survive rebuilds without anyone
    /// re-pasting them, which is also how secrets should reach a container.
    /// </summary>
    private string ResolveApiKey(ProviderConfig provider)
    {
        if (!string.IsNullOrWhiteSpace(provider.ApiKey)) return provider.ApiKey;

        var fromConfig = config[ProviderDefaults.ApiKeyConfigPath(provider.Slug)];
        if (!string.IsNullOrWhiteSpace(fromConfig)) return fromConfig;

        // Legacy single-provider setting, kept so existing OpenRouter deployments keep working.
        return provider.Slug.Equals("openrouter", StringComparison.OrdinalIgnoreCase)
            ? config["OpenRouter:ApiKey"] ?? ""
            : "";
    }
}
