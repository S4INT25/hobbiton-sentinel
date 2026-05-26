using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public class InMemoryFeedbackRuleStore : IFeedbackRuleStore
{
    private readonly List<FeedbackRule> _rules = [];

    public Task<List<FeedbackRule>> GetAllRulesAsync() =>
        Task.FromResult(_rules.OrderByDescending(r => r.CreatedAt).ToList());

    public Task<List<FeedbackRule>> GetActiveRulesAsync() =>
        Task.FromResult(_rules.Where(r => r.IsActive).ToList());

    public Task<FeedbackRule?> GetByIdAsync(string id) =>
        Task.FromResult(_rules.FirstOrDefault(r => r.Id == id));

    public Task SaveAsync(FeedbackRule rule)
    {
        _rules.RemoveAll(r => r.Id == rule.Id);
        _rules.Add(rule);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id)
    {
        _rules.RemoveAll(r => r.Id == id);
        return Task.CompletedTask;
    }

    public Task IncrementHitAsync(string id)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == id);
        if (rule != null) { rule.HitCount++; rule.LastHitAt = DateTime.UtcNow; }
        return Task.CompletedTask;
    }
}
