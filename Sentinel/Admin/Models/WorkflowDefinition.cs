namespace Sentinel.Admin.Models;

public class WorkflowDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ActionType { get; set; } = WorkflowActionTypes.EmailReport;
    public string CronExpression { get; set; } = "0 * * * *";
    public bool Enabled { get; set; } = true;
    public string TargetDatabase { get; set; } = "";
    public string EmailSubject { get; set; } = "";
    public string EmailRecipients { get; set; } = "";
    public string CustomPrompt { get; set; } = "";

    /// <summary>Custom system prompt preamble for fraud_run workflows. Replaces default Lipila context when set.</summary>
    public string SystemPrompt { get; set; } = "";

    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = "system";
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
