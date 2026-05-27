using System.Diagnostics.CodeAnalysis;
using Sentinel.Agent;
using Hangfire;
using Sentinel.Admin;

namespace Sentinel.Jobs;

[Queue("fraud")]
public class SentinelJob(FraudAgent agent, IActiveRunTracker runTracker, ILogger<SentinelJob> logger)
{
    
    public async Task RunAsync(string triggeredBy = "scheduler", string? runId = null, string? database = null,
        string? customPrompt = null)
    {
        var effectiveRunId = string.IsNullOrWhiteSpace(runId)
            ? DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")
            : runId;
        await runTracker.MarkRunningAsync(effectiveRunId, triggeredBy, DateTime.UtcNow);

        logger.LogInformation("Fraud detector job started at {Time} (triggered by: {TriggeredBy})",
            DateTime.UtcNow, triggeredBy);

        try
        {
            await agent.RunAsync(triggeredBy, effectiveRunId, database, customPrompt);
            await runTracker.MarkCompletedAsync(effectiveRunId);
            logger.LogInformation("Fraud detector job completed at {Time}", DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            await runTracker.MarkFailedAsync(effectiveRunId);
            logger.LogError(ex, "Fraud detector job failed");
            throw;
        }
    }
}
