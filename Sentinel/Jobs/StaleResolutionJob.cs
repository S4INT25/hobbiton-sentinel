using Hangfire;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;
using Sentinel.Memory;

namespace Sentinel.Jobs;

[Queue("fraud")]
public class StaleResolutionJob(
    ICaseStore caseStore,
    ICaseOutcomeStore caseOutcomeStore,
    IConfiguration config,
    ILogger<StaleResolutionJob> logger)
{
    public async Task RunAsync()
    {
        var staleDays = config.GetValue("Sentinel:StaleCase:ThresholdDays", 7);

        // Get stale cases before resolving so we can record outcomes
        var openCases = await caseStore.GetOpenCasesAsync();
        var cutoff = DateTime.UtcNow - TimeSpan.FromDays(staleDays);
        var staleCases = openCases.Where(c => c.LastSeen < cutoff).ToList();

        // Record auto_resolved outcomes before closing
        foreach (var c in staleCases)
        {
            await caseOutcomeStore.SaveAsync(new CaseOutcome
            {
                CaseId = c.Id,
                Title = c.Title,
                Category = c.Category,
                Outcome = "auto_resolved",
                OriginalSeverity = c.Severity,
                Confidence = c.Confidence,
                AffectedEntities = string.Join(", ", c.AffectedEntities),
                WorkflowId = c.WorkflowId,
                Resolution =
                    $"Auto-resolved: no agent activity for {staleDays}+ days (last seen {c.LastSeen:yyyy-MM-dd HH:mm} UTC)",
                ResolvedBy = "auto_stale",
                OccurrenceCount = c.OccurrenceCount
            });
        }

        var staleClosed = await caseStore.AutoResolveStaleAsync(staleDays);

        if (staleClosed > 0)
            logger.LogInformation("Auto-resolved {Count} stale case(s) (threshold: {Days} days)", staleClosed,
                staleDays);
        else
            logger.LogInformation("No stale cases to resolve (threshold: {Days} days)", staleDays);
    }
}