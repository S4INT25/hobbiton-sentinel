using Sentinel.Agent;
using Hangfire;
using Sentinel.Admin;

namespace Sentinel.Jobs;

[Queue("fraud")]
public class SentinelJob(FraudAgent agent, IActiveRunTracker runTracker, RunCancellationRegistry cancellation, ILogger<SentinelJob> logger)
{
    public async Task RunAsync(string triggeredBy = "scheduler", string? runId = null, string? database = null,
        string? customPrompt = null)
    {
        var effectiveRunId = string.IsNullOrWhiteSpace(runId)
            ? DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")
            : runId;

        var ct = cancellation.Register(effectiveRunId);
        await runTracker.MarkRunningAsync(effectiveRunId, triggeredBy, DateTime.UtcNow);

        logger.LogInformation("Fraud detector job started at {Time} (triggered by: {TriggeredBy})",
            DateTime.UtcNow, triggeredBy);

        try
        {
            ct.ThrowIfCancellationRequested();
            await agent.RunAsync(triggeredBy, effectiveRunId, database, customPrompt, ct);
            await runTracker.MarkCompletedAsync(effectiveRunId);
            logger.LogInformation("Fraud detector job completed at {Time}", DateTime.UtcNow);
        }
        catch (OperationCanceledException)
        {
            await runTracker.MarkStoppedAsync(effectiveRunId);
            logger.LogWarning("Fraud detector job {RunId} was stopped by user", effectiveRunId);
        }
        catch (Exception ex)
        {
            await runTracker.MarkFailedAsync(effectiveRunId);
            logger.LogError(ex, "Fraud detector job failed");
            throw;
        }
        finally
        {
            cancellation.Remove(effectiveRunId);
        }
    }
}
