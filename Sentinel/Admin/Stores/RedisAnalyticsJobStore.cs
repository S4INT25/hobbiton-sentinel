using System.Text.Json;
using StackExchange.Redis;

namespace Sentinel.Admin.Stores;

public class RedisAnalyticsJobStore(IConnectionMultiplexer redis, ILogger<RedisAnalyticsJobStore> logger) : IAnalyticsJobStore
{
    private readonly IDatabase _db = redis.GetDatabase();
    private static readonly TimeSpan JobTtl = TimeSpan.FromHours(24);

    private static string JobKey(string jobId) => $"sentinel:analytics:jobs:{jobId}";
    private static string UserJobsKey(string userId) => $"sentinel:analytics:jobs:user:{userId}";

    public async Task<AnalyticsQueryJob> CreateAsync(AnalyticsQueryJob job)
    {
        var json = JsonSerializer.Serialize(job);
        await _db.StringSetAsync(JobKey(job.Id), json, JobTtl);
        await _db.SortedSetAddAsync(UserJobsKey(job.UserId), job.Id, job.SubmittedAt.Ticks);
        logger.LogDebug("Job {JobId} created for user {UserId}", job.Id, job.UserId);
        return job;
    }

    public async Task<AnalyticsQueryJob?> GetAsync(string jobId)
    {
        var json = await _db.StringGetAsync(JobKey(jobId));
        if (json.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<AnalyticsQueryJob>((string)json!);
    }

    public async Task UpdateAsync(AnalyticsQueryJob job)
    {
        var json = JsonSerializer.Serialize(job);
        await _db.StringSetAsync(JobKey(job.Id), json, JobTtl);
    }

    public async Task<List<AnalyticsQueryJob>> GetUserJobsAsync(string userId, string? conversationId = null)
    {
        var jobIds = await _db.SortedSetRangeByRankAsync(UserJobsKey(userId), 0, 49, Order.Descending);
        var jobs = new List<AnalyticsQueryJob>();

        foreach (var id in jobIds)
        {
            var json = await _db.StringGetAsync(JobKey(id!));
            if (json.IsNullOrEmpty) continue;

            var job = JsonSerializer.Deserialize<AnalyticsQueryJob>((string)json!);
            if (job == null) continue;
            if (conversationId != null && job.ConversationId != conversationId) continue;

            jobs.Add(job);
        }

        return jobs;
    }
}
