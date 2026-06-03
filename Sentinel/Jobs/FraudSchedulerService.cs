using Hangfire;
using Sentinel.Agent;
using Sentinel.Admin.Models;

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
        var catZone = WorkflowTimeZones.ResolveOrDefault(WorkflowTimeZones.DefaultId);

        jobs.AddOrUpdate<SentinelJob>(
            recurringJobId: "fraud-detector-hourly",
            methodCall: job => job.RunAsync(new FraudAgentRunRequest { TriggeredBy = "scheduler" }),
            cronExpression: cron,
            queue:"fraud",
            options: new RecurringJobOptions
            {
                TimeZone = catZone
            });

        jobs.AddOrUpdate<StaleResolutionJob>(
            recurringJobId: "stale-case-resolution-daily",
            methodCall: job => job.RunAsync(),
            cronExpression: Cron.Daily(),
            queue: "fraud",
            options: new RecurringJobOptions
            {
                TimeZone = catZone
            });

        logger.LogInformation("Fraud detector scheduled: {Cron}", cron);
        await workflowScheduler.RefreshSchedulesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
