namespace Sentinel.Admin.Models;

public static class WorkflowDefaults
{
    public const string FraudRunWorkflowId = "seed-fraud-detection-run";

    public static IReadOnlyList<WorkflowDefinition> All =>
    [
        new WorkflowDefinition
        {
            Id = FraudRunWorkflowId,
            Name = "Default Fraud Detection",
            Description = "Runs the current Sentinel fraud detection pipeline. You can trigger it manually from Workflows or keep it scheduled.",
            ActionType = WorkflowActionTypes.FraudRun,
            CronExpression = "0 0 1 1 *",
            Enabled = true,
            TargetDatabase = "lipila_blaze",
            CreatedBy = "system"
        }
    ];
}
