using Hangfire;
using Sentinel.Admin;
using Sentinel.Agent;

namespace Sentinel.Jobs;

[Queue("fraud")]
public class SentinelJob(
    FraudAgent agent,
    IActiveRunTracker runTracker,
    RunCancellationRegistry cancellation,
    ILogger<SentinelJob> logger)
{
    public async Task RunAsync(FraudAgentRunRequest request)
    {
        var effectiveRunId = string.IsNullOrWhiteSpace(request.RunId)
            ? DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")
            : request.RunId;

        var ct = cancellation.Register(effectiveRunId);
        await runTracker.MarkRunningAsync(effectiveRunId, request.TriggeredBy, DateTime.UtcNow);

        logger.LogInformation("Fraud detector job started at {Time} (triggered by: {TriggeredBy})",
            DateTime.UtcNow, request.TriggeredBy);

        try
        {
            ct.ThrowIfCancellationRequested();
            await agent.RunAsync(request with { RunId = effectiveRunId }, ct);
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