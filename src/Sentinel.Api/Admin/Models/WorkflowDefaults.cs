namespace Sentinel.Admin.Models;

public static class WorkflowDefaults
{
    public const string FraudRunWorkflowId = "seed-fraud-detection-run";
    public const string PlatformActivityWorkflowId = "seed-platform-activity-digest";

    private static readonly string PlatformActivityPromptPath = Path.Combine(
        AppContext.BaseDirectory, "Templates", "platform-activity-prompt.md");

    /// <summary>
    /// The digest prompt lives in Templates/ so it can be tuned without a rebuild. If the file
    /// is missing the workflow is still seeded — disabled, with a prompt that says why — rather
    /// than silently emailing a half-configured digest every four hours.
    /// </summary>
    private static string PlatformActivityPrompt =>
        File.Exists(PlatformActivityPromptPath)
            ? File.ReadAllText(PlatformActivityPromptPath)
            : "Platform activity digest prompt not found at Templates/platform-activity-prompt.md. "
              + "Restore the file or paste the prompt here before enabling this workflow.";

    public static IReadOnlyList<WorkflowDefinition> All =>
    [
        new WorkflowDefinition
        {
            Id = FraudRunWorkflowId,
            Name = "Default Fraud Detection",
            Description =
                "Runs the current Sentinel fraud detection pipeline. You can trigger it manually from Workflows or keep it scheduled.",
            ActionType = WorkflowActionTypes.FraudRun,
            CronExpression = "0 0 1 1 *",
            TimeZoneId = WorkflowTimeZones.DefaultId,
            Enabled = true,
            TargetDatabase = "lipila_blaze",
            CreatedBy = "system"
        },
        new WorkflowDefinition
        {
            Id = PlatformActivityWorkflowId,
            Name = "Platform Activity Digest",
            Description =
                "Every 4 hours, summarises notable activity across Inshuwa, Gari, Lipila, BNPL and Patumba — "
                + "new records, money movement, unusual spikes, failed operations and security events. "
                + "An activity digest, not a metrics report.",
            ActionType = WorkflowActionTypes.EmailReport,
            CronExpression = "0 */4 * * *",
            TimeZoneId = WorkflowTimeZones.DefaultId,
            // Off until recipients are agreed — with none set it would fall back to Email:To
            // (the security mailbox) and put six digests a day in front of the wrong team.
            Enabled = false,
            // Spans every platform. Gari is the primary because it produces the most new records
            // per window, so its schema is the one the agent most needs loaded by default.
            TargetDatabase = "gari,inshuwa,lipila_blaze,bnpl,patumba_app",
            EmailSubject = "Platform Activity — last 4 hours",
            CustomPrompt = PlatformActivityPrompt,
            CreatedBy = "system"
        }
    ];
}
