using Hangfire;

namespace FraudDetector.Jobs;

public class FraudSchedulerService(
    IRecurringJobManager jobs,
    IConfiguration config,
    ILogger<FraudSchedulerService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cron = config["FraudDetector:CronSchedule"] ?? Cron.Hourly();

        jobs.AddOrUpdate<FraudDetectorJob>(
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
