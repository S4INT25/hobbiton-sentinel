using System.ComponentModel.DataAnnotations.Schema;

namespace Sentinel.Admin.Models;

/// <summary>
/// An LLM available to Sentinel agents. Admins can add, enable, disable,
/// and pick a default model from the UI without code changes. Chat users and workflows
/// select from the enabled models. Each model belongs to a <see cref="ProviderConfig"/>.
/// </summary>
[Table("llm_models")]
public class LlmModel
{
    [Column("id")] public int Id { get; set; }

    /// <summary>Human-friendly label shown in the UI (e.g. "Claude Sonnet 4.5").</summary>
    [Column("display_name")]
    public string DisplayName { get; set; } = "";

    /// <summary>The model id as the provider knows it (e.g. "anthropic/claude-sonnet-4.5").</summary>
    [Column("model_id")]
    public string ModelId { get; set; } = "";

    /// <summary>Short note on what this model is good for.</summary>
    [Column("description")]
    public string? Description { get; set; }

    [Column("provider_id")] public int ProviderId { get; set; }

    /// <summary>Navigation property used by EF only — not serialised.</summary>
    public ProviderConfig? Provider { get; set; }

    /// <summary>Only enabled models appear in the chat and workflow selectors.</summary>
    [Column("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Used when a chat message or workflow does not pick a model. At most one.</summary>
    [Column("is_default")]
    public bool IsDefault { get; set; }

    /// <summary>Display order in the dropdowns (lower = first).</summary>
    [Column("sort_order")]
    public int SortOrder { get; set; }

    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class LlmModelDefaults
{
    public static IReadOnlyList<LlmModel> All =>
    [
        new LlmModel
        {
            DisplayName = "DeepSeek V4 Flash", ModelId = "deepseek/deepseek-v4-flash",
            Description = "Fast and cheap — great for most tasks", Enabled = true, IsDefault = true, SortOrder = 0
        },
        new LlmModel
        {
            DisplayName = "DeepSeek V4 Pro", ModelId = "deepseek/deepseek-v4-pro",
            Description = "Deeper reasoning for complex analysis", Enabled = true, SortOrder = 1
        },
        new LlmModel
        {
            DisplayName = "Kimi K3", ModelId = "moonshotai/kimi-k3",
            Description = "Moonshot AI — strong long-context reasoning", Enabled = true, SortOrder = 2
        },
        new LlmModel
        {
            DisplayName = "Claude Opus 5 Fast", ModelId = "anthropic/claude-opus-5-fast",
            Description = "Anthropic top-tier reasoning", Enabled = true, SortOrder = 3
        },
    ];
}
