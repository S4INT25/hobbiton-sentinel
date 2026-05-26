using System.Collections.Concurrent;

namespace Sentinel.Admin.Stores;

public class InMemoryAnalyticsChatStore(ILogger<InMemoryAnalyticsChatStore> logger) : IAnalyticsChatStore
{
    private readonly ConcurrentDictionary<string, AnalyticsConversation> _conversations = new();

    private static string Key(string userId, string conversationId) => $"{userId}:{conversationId}";

    public Task<List<AnalyticsConversation>> ListConversationsAsync(string userId)
    {
        var conversations = _conversations.Values
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new AnalyticsConversation
            {
                Id = c.Id,
                Title = c.Title,
                Database = c.Database,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                UserId = c.UserId
            })
            .ToList();

        return Task.FromResult(conversations);
    }

    public Task<AnalyticsConversation?> GetConversationAsync(string userId, string conversationId)
    {
        _conversations.TryGetValue(Key(userId, conversationId), out var conversation);
        return Task.FromResult(conversation);
    }

    public Task<AnalyticsConversation> SaveConversationAsync(AnalyticsConversation conversation)
    {
        conversation.UpdatedAt = DateTime.UtcNow;
        _conversations[Key(conversation.UserId, conversation.Id)] = conversation;
        logger.LogDebug("Conversation {Id} saved for user {UserId}", conversation.Id, conversation.UserId);
        return Task.FromResult(conversation);
    }

    public Task DeleteConversationAsync(string userId, string conversationId)
    {
        _conversations.TryRemove(Key(userId, conversationId), out _);
        return Task.CompletedTask;
    }
}
