namespace Sentinel.Admin.Stores;

public interface IAnalyticsChatStore
{
    Task<List<AnalyticsConversation>> ListConversationsAsync(string userId);
    Task<AnalyticsConversation?> GetConversationAsync(string userId, string conversationId);
    Task<AnalyticsConversation> SaveConversationAsync(AnalyticsConversation conversation);
    Task DeleteConversationAsync(string userId, string conversationId);
    Task ShareConversationAsync(AnalyticsConversation conversation);
    Task UnshareConversationAsync(string conversationId);
    Task<AnalyticsConversation?> GetSharedConversationAsync(string conversationId);
}