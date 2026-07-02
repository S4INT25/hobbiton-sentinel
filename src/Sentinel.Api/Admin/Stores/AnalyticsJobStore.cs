using ZiggyCreatures.Caching.Fusion;

namespace Sentinel.Admin.Stores;

public class AnalyticsJobStore(IFusionCache cache, ILogger<AnalyticsJobStore> logger) : IAnalyticsJobStore
{
    private static readonly TimeSpan JobTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan IndexTtl = TimeSpan.FromDays(7);

    public async Task<AnalyticsQueryJob> CreateAsync(AnalyticsQueryJob job)
    {
        await cache.SetAsync(JobKey(job.Id), job, o => o.SetDuration(JobTtl));

        var ids = await cache.GetOrDefaultAsync<List<string>>(UserIndexKey(job.UserId)) ?? [];
        ids.Insert(0, job.Id);
        await cache.SetAsync(UserIndexKey(job.UserId), ids, o => o.SetDuration(IndexTtl));

        logger.LogDebug("Job {JobId} created for user {UserId}", job.Id, job.UserId);
        return job;
    }

    public async Task<AnalyticsQueryJob?> GetAsync(string jobId) =>
        await cache.GetOrDefaultAsync<AnalyticsQueryJob>(JobKey(jobId));

    public async Task UpdateAsync(AnalyticsQueryJob job) =>
        await cache.SetAsync(JobKey(job.Id), job, o => o.SetDuration(JobTtl));

    public async Task<List<AnalyticsQueryJob>> GetUserJobsAsync(string userId, string? conversationId = null)
    {
        var ids = await cache.GetOrDefaultAsync<List<string>>(UserIndexKey(userId)) ?? [];
        var jobs = new List<AnalyticsQueryJob>();

        foreach (var id in ids.Take(50))
        {
            var job = await cache.GetOrDefaultAsync<AnalyticsQueryJob>(JobKey(id));
            if (job == null) continue;
            if (conversationId != null && job.ConversationId != conversationId) continue;
            jobs.Add(job);
        }

        return jobs;
    }

    private static string JobKey(string jobId) => $"sentinel:analytics:job:{jobId}";
    private static string UserIndexKey(string userId) => $"sentinel:analytics:jobs:user:{userId}";
}