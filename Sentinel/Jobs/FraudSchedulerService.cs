using Hangfire;
using Sentinel.Agent;

namespace Sentinel.Jobs;

public class FraudSchedulerService(
    IRecurringJobManager jobs,
    IConfiguration config,
    WorkflowSchedulerService workflowScheduler,
    ILogger<FraudSchedulerService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var cron = config["Sentinel:CronSchedule"] ?? Cron.Hourly();

        jobs.AddOrUpdate<SentinelJob>(
            recurringJobId: "fraud-detector-hourly",
            methodCall: job => job.RunAsync(new FraudAgentRunRequest { TriggeredBy = "scheduler" }),
            cronExpression: cron,
            queue:"fraud",
            options: new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        jobs.AddOrUpdate<StaleResolutionJob>(
            recurringJobId: "stale-case-resolution-daily",
            methodCall: job => job.RunAsync(),
            cronExpression: Cron.Daily(),
            queue: "fraud",
            options: new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        logger.LogInformation("Fraud detector scheduled: {Cron}", cron);
        await workflowScheduler.RefreshSchedulesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
