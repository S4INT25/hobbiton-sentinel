using Sentinel.Admin.Models;
using ZiggyCreatures.Caching.Fusion;

namespace Sentinel.Admin.Stores;

public class FeedbackRuleStore(IFusionCache cache) : IFeedbackRuleStore
{
    private const string Key = "sentinel:rules";

    public async Task<List<FeedbackRule>> GetAllRulesAsync() =>
        (await LoadAsync()).OrderByDescending(r => r.CreatedAt).ToList();

    public async Task<List<FeedbackRule>> GetActiveRulesAsync() =>
        (await LoadAsync()).Where(r => r.IsActive).ToList();

    public async Task<FeedbackRule?> GetByIdAsync(string id) =>
        (await LoadAsync()).FirstOrDefault(r => r.Id == id);

    public async Task SaveAsync(FeedbackRule rule)
    {
        var rules = await LoadAsync();
        rules.RemoveAll(r => r.Id == rule.Id);
        rules.Add(rule);
        await PersistAsync(rules);
    }

    public async Task DeleteAsync(string id)
    {
        var rules = await LoadAsync();
        rules.RemoveAll(r => r.Id == id);
        await PersistAsync(rules);
    }

    public async Task IncrementHitAsync(string id)
    {
        var rules = await LoadAsync();
        var rule = rules.FirstOrDefault(r => r.Id == id);
        if (rule != null)
        {
            rule.HitCount++;
            rule.LastHitAt = DateTime.UtcNow;
        }

        await PersistAsync(rules);
    }

    private async Task<List<FeedbackRule>> LoadAsync() =>
        await cache.GetOrDefaultAsync<List<FeedbackRule>>(Key) ?? [];

    private Task PersistAsync(List<FeedbackRule> rules) =>
        cache.SetAsync(Key, rules, o => o.SetDuration(TimeSpan.MaxValue)).AsTask();
}