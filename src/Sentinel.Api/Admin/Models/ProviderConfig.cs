using System.ComponentModel.DataAnnotations.Schema;

namespace Sentinel.Admin.Models;

/// <summary>
/// An LLM inference provider. Each <see cref="LlmModel"/> belongs to a provider,
/// so you can mix OpenRouter models with direct-API models (DeepSeek, OpenAI, etc.)
/// from the admin portal.
/// </summary>
[Table("providers")]
public class ProviderConfig
{
    [Column("id")] public int Id { get; set; }

    /// <summary>Human-friendly label (e.g. "OpenRouter", "DeepSeek").</summary>
    [Column("display_name")]
    public string DisplayName { get; set; } = "";

    /// <summary>Short machine identifier (e.g. "openrouter", "deepseek"). Unique.</summary>
    [Column("slug")]
    public string Slug { get; set; } = "";

    /// <summary>API key for this provider.</summary>
    [Column("api_key")]
    public string ApiKey { get; set; } = "";

    /// <summary>OpenAI-compatible endpoint URL (e.g. https://api.deepseek.com).</summary>
    [Column("endpoint")]
    public string Endpoint { get; set; } = "";

    [Column("enabled")] public bool Enabled { get; set; } = true;
    [Column("is_default")] public bool IsDefault { get; set; }
    [Column("sort_order")] public int SortOrder { get; set; }

    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class ProviderDefaults
{
    /// <summary>
    /// DeepSeek's direct API. OpenAI-compatible, so it needs no client changes.
    /// Default provider — the models Sentinel runs on are DeepSeek's.
    /// </summary>
    public static readonly ProviderConfig DeepSeek = new()
    {
        DisplayName = "DeepSeek", Slug = "deepseek",
        ApiKey = "", Endpoint = "https://api.deepseek.com/v1",
        Enabled = true, IsDefault = true, SortOrder = 0
    };

    public static readonly ProviderConfig OpenRouter = new()
    {
        DisplayName = "OpenRouter", Slug = "openrouter",
        ApiKey = "", Endpoint = "https://openrouter.ai/api/v1",
        Enabled = true, IsDefault = false, SortOrder = 1
    };

    public static IReadOnlyList<ProviderConfig> All => [DeepSeek, OpenRouter];

    /// <summary>
    /// Configuration key holding a provider's API key, e.g. <c>Providers:deepseek:ApiKey</c>
    /// (env var <c>Providers__deepseek__ApiKey</c>).
    ///
    /// Keys are read from configuration when the database row has none, so credentials live in
    /// the deployment environment rather than only in a row a redeploy can lose.
    /// </summary>
    public static string ApiKeyConfigPath(string slug) => $"Providers:{slug}:ApiKey";

    /// <summary>The same setting as an environment variable name, for error messages and docs.</summary>
    public static string ApiKeyEnvVar(string slug) => $"Providers__{slug}__ApiKey";
}
