using System.Text.Json;
using Sentinel.Admin.Models;
using StackExchange.Redis;

namespace Sentinel.Admin.Stores;

public class FeedbackRuleStore(IConnectionMultiplexer redis) : IFeedbackRuleStore
{
    private readonly IDatabase _db = redis.GetDatabase();
    private const string SetKey = "sentinel:rules";
    private const string Prefix = "sentinel:rule:";

    public async Task<List<FeedbackRule>> GetAllRulesAsync()
    {
        var ids = await _db.SetMembersAsync(SetKey);
        var rules = new List<FeedbackRule>();

        foreach (var id in ids)
        {
            var json = await _db.StringGetAsync($"{Prefix}{id}");
            if (json.IsNullOrEmpty) continue;
            var rule = JsonSerializer.Deserialize<FeedbackRule>((string)json!);
            if (rule != null) rules.Add(rule);
        }

        return rules.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public async Task<List<FeedbackRule>> GetActiveRulesAsync()
    {
        var all = await GetAllRulesAsync();
        return all.Where(r => r.IsActive).ToList();
    }

    public async Task<FeedbackRule?> GetByIdAsync(string id)
    {
        var json = await _db.StringGetAsync($"{Prefix}{id}");
        return json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<FeedbackRule>((string)json!);
    }

    public async Task SaveAsync(FeedbackRule rule)
    {
        var json = JsonSerializer.Serialize(rule);
        await _db.StringSetAsync($"{Prefix}{rule.Id}", json);
        await _db.SetAddAsync(SetKey, rule.Id);
    }

    public async Task DeleteAsync(string id)
    {
        await _db.KeyDeleteAsync($"{Prefix}{id}");
        await _db.SetRemoveAsync(SetKey, id);
    }

    public async Task IncrementHitAsync(string id)
    {
        var rule = await GetByIdAsync(id);
        if (rule == null) return;
        rule.HitCount++;
        rule.LastHitAt = DateTime.UtcNow;
        await SaveAsync(rule);
    }
}
