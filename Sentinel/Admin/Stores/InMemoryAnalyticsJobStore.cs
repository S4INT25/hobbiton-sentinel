using System.Collections.Concurrent;

namespace Sentinel.Admin.Stores;

public class InMemoryAnalyticsJobStore(ILogger<InMemoryAnalyticsJobStore> logger) : IAnalyticsJobStore
{
    private readonly ConcurrentDictionary<string, AnalyticsQueryJob> _jobs = new();

    public Task<AnalyticsQueryJob> CreateAsync(AnalyticsQueryJob job)
    {
        _jobs[job.Id] = job;
        logger.LogDebug("Job {JobId} created for user {UserId}", job.Id, job.UserId);
        return Task.FromResult(job);
    }

    public Task<AnalyticsQueryJob?> GetAsync(string jobId)
    {
        _jobs.TryGetValue(jobId, out var job);
        return Task.FromResult(job);
    }

    public Task UpdateAsync(AnalyticsQueryJob job)
    {
        _jobs[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task<List<AnalyticsQueryJob>> GetUserJobsAsync(string userId, string? conversationId = null)
    {
        var jobs = _jobs.Values
            .Where(j => j.UserId == userId)
            .Where(j => conversationId == null || j.ConversationId == conversationId)
            .OrderByDescending(j => j.SubmittedAt)
            .ToList();
        return Task.FromResult(jobs);
    }
}
