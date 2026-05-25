using System.Diagnostics.CodeAnalysis;
using Hangfire;

namespace Sentinel.Jobs;

public class FraudSchedulerService(
    IRecurringJobManager jobs,
    IConfiguration config,
    ILogger<FraudSchedulerService> logger) : IHostedService
{
    [Experimental("SCME0001")]
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cron = config["Sentinel:CronSchedule"] ?? Cron.Hourly();

        jobs.AddOrUpdate<SentinelJob>(
            recurringJobId: "fraud-detector-hourly",
            methodCall: job => job.RunAsync(),
            cronExpression: cron,
            queue:"fraud",
            options: new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        logger.LogInformation("Fraud detector scheduled: {Cron}", cron);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
