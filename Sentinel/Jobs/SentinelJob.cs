using System.Diagnostics.CodeAnalysis;
using Sentinel.Agent;
using Hangfire;

namespace Sentinel.Jobs;

[Queue("fraud")]
public class SentinelJob(FraudAgent agent, ILogger<SentinelJob> logger)
{
    [Experimental("SCME0001")]
    public async Task RunAsync()
    {
        logger.LogInformation("Fraud detector job started at {Time}", DateTime.UtcNow);

        try
        {
            await agent.RunAsync();
            logger.LogInformation("Fraud detector job completed at {Time}", DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fraud detector job failed");
            throw; // Let Hangfire handle retry
        }
    }
}
