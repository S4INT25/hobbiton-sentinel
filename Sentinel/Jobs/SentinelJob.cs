using System.Diagnostics.CodeAnalysis;
using Sentinel.Agent;
using Hangfire;
using Sentinel.Admin;

namespace Sentinel.Jobs;

[Queue("fraud")]
public class SentinelJob(FraudAgent agent, ActiveRunTracker runTracker, ILogger<SentinelJob> logger)
{
    
    public async Task RunAsync(string triggeredBy = "scheduler", string? runId = null)
    {
        var effectiveRunId = string.IsNullOrWhiteSpace(runId)
            ? DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")
            : runId;
        runTracker.MarkRunning(effectiveRunId, triggeredBy, DateTime.UtcNow);

        logger.LogInformation("Fraud detector job started at {Time} (triggered by: {TriggeredBy})",
            DateTime.UtcNow, triggeredBy);

        try
        {
            await agent.RunAsync(triggeredBy, effectiveRunId);
            runTracker.MarkCompleted(effectiveRunId);
            logger.LogInformation("Fraud detector job completed at {Time}", DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            runTracker.MarkFailed(effectiveRunId);
            logger.LogError(ex, "Fraud detector job failed");
            throw;
        }
    }
}
