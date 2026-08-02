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
    public static readonly ProviderConfig OpenRouter = new()
    {
        DisplayName = "OpenRouter", Slug = "openrouter",
        ApiKey = "", Endpoint = "https://openrouter.ai/api/v1",
        Enabled = true, IsDefault = true, SortOrder = 0
    };

    /// <summary>
    /// DeepSeek's direct API, which is OpenAI-compatible so it needs no client changes.
    /// Seeded disabled: it ships without a key, and an enabled keyless provider can be picked
    /// up by the resolver's last-resort fallback and fail at call time. Paste a key on the
    /// Models &amp; Providers page, then enable it — the seeder will not switch it back off.
    /// </summary>
    public static readonly ProviderConfig DeepSeek = new()
    {
        DisplayName = "DeepSeek", Slug = "deepseek",
        ApiKey = "", Endpoint = "https://api.deepseek.com/v1",
        Enabled = false, IsDefault = false, SortOrder = 1
    };

    public static IReadOnlyList<ProviderConfig> All => [OpenRouter, DeepSeek];
}
