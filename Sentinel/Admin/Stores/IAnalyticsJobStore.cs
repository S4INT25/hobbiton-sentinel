namespace Sentinel.Admin.Stores;

public interface IAnalyticsJobStore
{
    Task<AnalyticsQueryJob> CreateAsync(AnalyticsQueryJob job);
    Task<AnalyticsQueryJob?> GetAsync(string jobId);
    Task UpdateAsync(AnalyticsQueryJob job);
    Task<List<AnalyticsQueryJob>> GetUserJobsAsync(string userId, string? conversationId = null);
}
