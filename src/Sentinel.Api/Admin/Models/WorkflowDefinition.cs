namespace Sentinel.Admin.Models;

public class WorkflowDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ActionType { get; set; } = WorkflowActionTypes.EmailReport;
    public string CronExpression { get; set; } = "0 * * * *";
    public string TimeZoneId { get; set; } = WorkflowTimeZones.DefaultId;
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// Comma-separated list of ClickHouse databases this workflow may query. The first entry is the
    /// primary — it supplies the agent's default schema and the memories loaded for the run.
    /// Stored as one string (like <see cref="EmailRecipients"/>) so multi-database support needed no
    /// schema change and existing single-database rows keep working untouched.
    /// </summary>
    public string TargetDatabase { get; set; } = "";
    public string EmailSubject { get; set; } = "";
    public string EmailRecipients { get; set; } = "";
    public string CustomPrompt { get; set; } = "";

    /// <summary>Custom system prompt preamble for fraud_run workflows. Replaces default Lipila context when set.</summary>
    public string SystemPrompt { get; set; } = "";

    /// <summary>OpenRouter model id override (e.g. "anthropic/claude-sonnet-4.5"). Empty = system default model.</summary>
    public string Model { get; set; } = "";

    /// <summary>Reasoning effort override ("low"|"medium"|"high"). Empty = model default.</summary>
    public string ReasoningEffort { get; set; } = "";

    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = "system";
}

public static class WorkflowDatabases
{
    /// <summary>Splits the stored list into distinct, trimmed database names, order preserved.</summary>
    public static IReadOnlyList<string> Parse(string? targetDatabase) =>
        string.IsNullOrWhiteSpace(targetDatabase)
            ? []
            : [.. targetDatabase
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The agent's default database — the first entry, or <paramref name="fallback"/> if none.</summary>
    public static string Primary(string? targetDatabase, string fallback = "lipila_blaze") =>
        Parse(targetDatabase).FirstOrDefault() ?? fallback;

    /// <summary>Everything after the primary. Empty for a single-database workflow.</summary>
    public static IReadOnlyList<string> Secondary(string? targetDatabase) =>
        [.. Parse(targetDatabase).Skip(1)];

    /// <summary>Round-trips the list back into the stored form, deduped and trimmed.</summary>
    public static string Normalize(string? targetDatabase) =>
        string.Join(",", Parse(targetDatabase));
}

public static class WorkflowActionTypes
{
    public const string EmailReport = "email_report";
    public const string LegacySqlEmailReport = "sql_email_report";
    public const string FraudRun = "fraud_run";

    public static readonly IReadOnlyList<string> All = [EmailReport, FraudRun];

    public static string Normalize(string? actionType)
    {
        var normalized = (actionType ?? "").Trim().ToLowerInvariant();
        return normalized == LegacySqlEmailReport ? EmailReport : normalized;
    }
}