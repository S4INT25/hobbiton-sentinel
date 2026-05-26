using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public interface IFeedbackRuleStore
{
    Task<List<FeedbackRule>> GetActiveRulesAsync();
    Task<List<FeedbackRule>> GetAllRulesAsync();
    Task<FeedbackRule?> GetByIdAsync(string id);
    Task SaveAsync(FeedbackRule rule);
    Task DeleteAsync(string id);
    Task IncrementHitAsync(string id);
}
