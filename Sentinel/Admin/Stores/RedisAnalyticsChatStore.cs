using System.Text.Json;
using StackExchange.Redis;

namespace Sentinel.Admin.Stores;

public class RedisAnalyticsChatStore(IConnectionMultiplexer redis, ILogger<RedisAnalyticsChatStore> logger) : IAnalyticsChatStore
{
    private readonly IDatabase _db = redis.GetDatabase();
    private static readonly TimeSpan ConversationTtl = TimeSpan.FromDays(90);

    private static string ConversationKey(string userId, string conversationId) =>
        $"sentinel:analytics:conversations:{userId}:{conversationId}";

    private static string IndexKey(string userId) =>
        $"sentinel:analytics:conversations:{userId}:index";

    public async Task<List<AnalyticsConversation>> ListConversationsAsync(string userId)
    {
        var indexKey = IndexKey(userId);
        var conversationIds = await _db.SetMembersAsync(indexKey);
        var conversations = new List<AnalyticsConversation>();

        foreach (var id in conversationIds)
        {
            var json = await _db.StringGetAsync(ConversationKey(userId, id!));
            if (json.IsNullOrEmpty)
            {
                await _db.SetRemoveAsync(indexKey, id);
                continue;
            }

            var conversation = JsonSerializer.Deserialize<AnalyticsConversation>((string)json!);
            if (conversation == null) continue;

            conversations.Add(new AnalyticsConversation
            {
                Id = conversation.Id,
                Title = conversation.Title,
                Database = conversation.Database,
                CreatedAt = conversation.CreatedAt,
                UpdatedAt = conversation.UpdatedAt,
                UserId = conversation.UserId
            });
        }

        return conversations.OrderByDescending(c => c.UpdatedAt).ToList();
    }

    public async Task<AnalyticsConversation?> GetConversationAsync(string userId, string conversationId)
    {
        var json = await _db.StringGetAsync(ConversationKey(userId, conversationId));
        if (json.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<AnalyticsConversation>((string)json!);
    }

    public async Task<AnalyticsConversation> SaveConversationAsync(AnalyticsConversation conversation)
    {
        conversation.UpdatedAt = DateTime.UtcNow;
        var key = ConversationKey(conversation.UserId, conversation.Id);
        var json = JsonSerializer.Serialize(conversation);

        await _db.StringSetAsync(key, json, ConversationTtl);
        await _db.SetAddAsync(IndexKey(conversation.UserId), conversation.Id);

        logger.LogDebug("Conversation {Id} saved for user {UserId}", conversation.Id, conversation.UserId);
        return conversation;
    }

    public async Task DeleteConversationAsync(string userId, string conversationId)
    {
        await _db.KeyDeleteAsync(ConversationKey(userId, conversationId));
        await _db.SetRemoveAsync(IndexKey(userId), conversationId);
    }
}
