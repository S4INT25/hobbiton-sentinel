using FraudDetector.Agent;
using Hangfire;

namespace FraudDetector.Jobs;

[Queue("fraud")]
public class FraudDetectorJob(FraudAgent agent, ILogger<FraudDetectorJob> logger)
{
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
