using Hangfire;
using Sentinel.Memory;

namespace Sentinel.Jobs;

[Queue("fraud")]
public class StaleResolutionJob(
    ICaseStore caseStore,
    IConfiguration config,
    ILogger<StaleResolutionJob> logger)
{
    public async Task RunAsync()
    {
        var staleDays = config.GetValue("Sentinel:StaleCase:ThresholdDays", 7);
        var staleClosed = await caseStore.AutoResolveStaleAsync(staleDays);

        if (staleClosed > 0)
            logger.LogInformation("Auto-resolved {Count} stale case(s) (threshold: {Days} days)", staleClosed, staleDays);
        else
            logger.LogInformation("No stale cases to resolve (threshold: {Days} days)", staleDays);
    }
}
