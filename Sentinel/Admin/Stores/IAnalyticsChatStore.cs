namespace Sentinel.Admin.Stores;

public interface IAnalyticsChatStore
{
    Task<List<AnalyticsConversation>> ListConversationsAsync(string userId);
    Task<AnalyticsConversation?> GetConversationAsync(string userId, string conversationId);
    Task<AnalyticsConversation> SaveConversationAsync(AnalyticsConversation conversation);
    Task DeleteConversationAsync(string userId, string conversationId);
}
