using Sentinel.Admin.Models;

namespace Sentinel.Tests.Admin;

/// <summary>
/// Provider and model seeding. The pairing matters: a DeepSeek model id is invalid on OpenRouter
/// and vice versa, so a mis-attached model 404s at the vendor rather than failing loudly here.
/// </summary>
public class ProviderDefaultsTests
{
    [Fact]
    public void DeepSeek_is_the_default_provider()
    {
        var defaults = ProviderDefaults.All;
        var byDefault = defaults.Where(p => p.IsDefault).ToList();

        Assert.Single(byDefault);
        Assert.Equal("deepseek", byDefault[0].Slug);
    }

    [Fact]
    public void Seeded_providers_are_enabled_and_have_endpoints()
    {
        foreach (var p in ProviderDefaults.All)
        {
            Assert.True(p.Enabled, $"{p.Slug} is seeded disabled and would be unusable.");
            Assert.False(string.IsNullOrWhiteSpace(p.Endpoint), $"{p.Slug} has no endpoint.");
            Assert.True(Uri.TryCreate(p.Endpoint, UriKind.Absolute, out _), $"{p.Slug} endpoint is not absolute.");
        }
    }

    [Fact]
    public void Provider_slugs_are_unique()
    {
        var slugs = ProviderDefaults.All.Select(p => p.Slug).ToList();
        Assert.Equal(slugs.Count, slugs.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void No_api_key_is_committed_to_source()
    {
        // Keys belong in the environment, never in the seed.
        Assert.All(ProviderDefaults.All, p => Assert.Equal("", p.ApiKey));
    }

    [Theory]
    [InlineData("deepseek", "Providers:deepseek:ApiKey")]
    [InlineData("openrouter", "Providers:openrouter:ApiKey")]
    public void Api_key_config_path_matches_the_documented_env_var(string slug, string expected)
    {
        // Env binding turns ':' into '__', so this is Providers__deepseek__ApiKey in compose.
        Assert.Equal(expected, ProviderDefaults.ApiKeyConfigPath(slug));
    }

    [Fact]
    public void Every_seeded_model_points_at_a_seeded_provider()
    {
        var slugs = ProviderDefaults.All.Select(p => p.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in LlmModelDefaults.Seeded)
            Assert.True(slugs.Contains(entry.ProviderSlug),
                $"{entry.Model.ModelId} names provider '{entry.ProviderSlug}', which is not seeded.");
    }

    [Fact]
    public void DeepSeek_models_use_direct_api_ids_not_openrouter_paths()
    {
        // DeepSeek's own API expects "deepseek-v4-flash"; "deepseek/deepseek-v4-flash" is the
        // OpenRouter route and 404s against api.deepseek.com.
        foreach (var entry in LlmModelDefaults.Seeded.Where(m => m.ProviderSlug == "deepseek"))
            Assert.False(entry.Model.ModelId.Contains('/'),
                $"{entry.Model.ModelId} looks like an OpenRouter path but is attached to DeepSeek.");
    }

    [Fact]
    public void OpenRouter_models_use_vendor_prefixed_ids()
    {
        foreach (var entry in LlmModelDefaults.Seeded.Where(m => m.ProviderSlug == "openrouter"))
            Assert.True(entry.Model.ModelId.Contains('/'),
                $"{entry.Model.ModelId} is on OpenRouter but has no vendor prefix.");
    }

    [Fact]
    public void Exactly_one_model_is_default_and_it_is_a_deepseek_model()
    {
        var defaults = LlmModelDefaults.Seeded.Where(m => m.Model.IsDefault).ToList();

        Assert.Single(defaults);
        Assert.Equal("deepseek", defaults[0].ProviderSlug);
        Assert.Equal("deepseek-v4-flash", defaults[0].Model.ModelId);
    }

    [Fact]
    public void The_available_deepseek_models_are_seeded()
    {
        var ids = LlmModelDefaults.Seeded
            .Where(m => m.ProviderSlug == "deepseek")
            .Select(m => m.Model.ModelId)
            .Order()
            .ToList();

        Assert.Equal(["deepseek-v4-flash", "deepseek-v4-pro"], ids);
    }

    [Fact]
    public void Model_ids_are_unique()
    {
        var ids = LlmModelDefaults.All.Select(m => m.ModelId).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
