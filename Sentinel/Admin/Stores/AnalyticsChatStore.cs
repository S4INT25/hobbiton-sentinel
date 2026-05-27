using ZiggyCreatures.Caching.Fusion;

namespace Sentinel.Admin.Stores;

public class AnalyticsChatStore(IFusionCache cache, ILogger<AnalyticsChatStore> logger) : IAnalyticsChatStore
{
    private static readonly TimeSpan ConversationTtl = TimeSpan.FromDays(90);

    private static string ConversationKey(string userId, string id) =>
        $"sentinel:analytics:conversations:{userId}:{id}";

    private static string UserListKey(string userId) =>
        $"sentinel:analytics:conversations:{userId}:all";

    public async Task<List<AnalyticsConversation>> ListConversationsAsync(string userId) =>
        (await cache.GetOrDefaultAsync<List<AnalyticsConversation>>(UserListKey(userId)) ?? [])
            .OrderByDescending(c => c.UpdatedAt)
            .ToList();

    public async Task<AnalyticsConversation?> GetConversationAsync(string userId, string conversationId) =>
        await cache.GetOrDefaultAsync<AnalyticsConversation>(ConversationKey(userId, conversationId));

    public async Task<AnalyticsConversation> SaveConversationAsync(AnalyticsConversation conversation)
    {
        conversation.UpdatedAt = DateTime.UtcNow;

        await cache.SetAsync(
            ConversationKey(conversation.UserId, conversation.Id),
            conversation,
            o => o.SetDuration(ConversationTtl));

        // Update the user's list with a stub (no Messages to keep it lean)
        var stubs = await cache.GetOrDefaultAsync<List<AnalyticsConversation>>(UserListKey(conversation.UserId)) ?? [];
        stubs.RemoveAll(c => c.Id == conversation.Id);
        stubs.Insert(0, new AnalyticsConversation
        {
            Id = conversation.Id,
            Title = conversation.Title,
            Database = conversation.Database,
            CreatedAt = conversation.CreatedAt,
            UpdatedAt = conversation.UpdatedAt,
            UserId = conversation.UserId
        });
        await cache.SetAsync(UserListKey(conversation.UserId), stubs, o => o.SetDuration(ConversationTtl));

        logger.LogDebug("Conversation {Id} saved for user {UserId}", conversation.Id, conversation.UserId);
        return conversation;
    }

    public async Task DeleteConversationAsync(string userId, string conversationId)
    {
        await cache.RemoveAsync(ConversationKey(userId, conversationId));

        var stubs = await cache.GetOrDefaultAsync<List<AnalyticsConversation>>(UserListKey(userId)) ?? [];
        stubs.RemoveAll(c => c.Id == conversationId);
        await cache.SetAsync(UserListKey(userId), stubs, o => o.SetDuration(ConversationTtl));
    }
}
