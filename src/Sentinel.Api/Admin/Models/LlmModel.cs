using System.ComponentModel.DataAnnotations.Schema;

namespace Sentinel.Admin.Models;

/// <summary>
/// An LLM available to Sentinel agents via OpenRouter. Admins can add, enable, disable,
/// and pick a default model from the UI without code changes. Chat users and workflows
/// select from the enabled models.
/// </summary>
[Table("llm_models")]
public class LlmModel
{
    [Column("id")] public int Id { get; set; }

    /// <summary>Human-friendly label shown in the UI (e.g. "Claude Sonnet 4.5").</summary>
    [Column("display_name")]
    public string DisplayName { get; set; } = "";

    /// <summary>The OpenRouter model id (e.g. "anthropic/claude-sonnet-4.5").</summary>
    [Column("model_id")]
    public string ModelId { get; set; } = "";

    /// <summary>Short note on what this model is good for.</summary>
    [Column("description")]
    public string? Description { get; set; }

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
            DisplayName = "Claude Sonnet 4.5", ModelId = "anthropic/claude-sonnet-4.5",
            Description = "Best all-round reasoning and analysis", Enabled = true, IsDefault = true, SortOrder = 0
        },
        new LlmModel
        {
            DisplayName = "GPT-5", ModelId = "openai/gpt-5",
            Description = "OpenAI flagship — strong general reasoning", Enabled = true, SortOrder = 1
        },
        new LlmModel
        {
            DisplayName = "Gemini 2.5 Pro", ModelId = "google/gemini-2.5-pro",
            Description = "Google flagship — long context, fast", Enabled = true, SortOrder = 2
        },
        new LlmModel
        {
            DisplayName = "DeepSeek V3.1", ModelId = "deepseek/deepseek-chat-v3.1",
            Description = "Low-cost workhorse for scheduled runs", Enabled = true, SortOrder = 3
        },
        new LlmModel
        {
            DisplayName = "Llama 3.3 70B", ModelId = "meta-llama/llama-3.3-70b-instruct",
            Description = "Open-weight, cheap and capable", Enabled = true, SortOrder = 4
        },
    ];
}
