using System.Diagnostics.CodeAnalysis;
using Sentinel.Agent;
using Hangfire;

namespace Sentinel.Jobs;

[Queue("fraud")]
public class SentinelJob(FraudAgent agent, ILogger<SentinelJob> logger)
{
   
    public async Task RunAsync(string triggeredBy = "scheduler")
    {
        logger.LogInformation("Fraud detector job started at {Time} (triggered by: {TriggeredBy})",
            DateTime.UtcNow, triggeredBy);

        try
        {
            await agent.RunAsync(triggeredBy);
            logger.LogInformation("Fraud detector job completed at {Time}", DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fraud detector job failed");
            throw;
        }
    }
}
