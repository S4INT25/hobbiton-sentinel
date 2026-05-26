using System.Text.Json;
using Sentinel.Admin.Stores;
using Sentinel.Infrastructure;

namespace Sentinel.Admin;

public static class AdminApiEndpoints
{
    private static readonly HashSet<string> SystemDatabases =
        ["system", "INFORMATION_SCHEMA", "information_schema", "default"];

    public static void MapAdminApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/analytics");

        api.MapGet("/databases", async (ClickHouseClient ch) =>
        {
            var json = await ch.QueryAsync("SHOW DATABASES");
            var databases = ParseDatabaseNames(json)
                .Where(db => !SystemDatabases.Contains(db))
                .OrderBy(db => db)
                .ToList();
            return Results.Ok(databases);
        });

        api.MapGet("/conversations", async (IAnalyticsChatStore store) =>
        {
            var userId = "default"; // simplified — extend with auth later
            var conversations = await store.ListConversationsAsync(userId);
            return Results.Ok(conversations);
        });

        api.MapGet("/conversations/{id}", async (string id, IAnalyticsChatStore store) =>
        {
            var userId = "default";
            var conversation = await store.GetConversationAsync(userId, id);
            return conversation is null ? Results.NotFound() : Results.Ok(conversation);
        });

        api.MapPost("/conversations", async (CreateConversationRequest request, IAnalyticsChatStore store) =>
        {
            var conversation = new AnalyticsConversation
            {
                Database = request.Database,
                UserId = "default"
            };
            await store.SaveConversationAsync(conversation);
            return Results.Ok(conversation);
        });

        api.MapDelete("/conversations/{id}", async (string id, IAnalyticsChatStore store) =>
        {
            var userId = "default";
            await store.DeleteConversationAsync(userId, id);
            return Results.NoContent();
        });

        api.MapPost("/ask", async (AnalyticsAskRequest request, AnalyticsAgent agent, IAnalyticsChatStore store) =>
        {
            var userId = "default";
            AnalyticsConversation? conversation = null;
            List<ChatEntry>? history = null;

            // Load existing conversation for context
            if (!string.IsNullOrEmpty(request.ConversationId))
            {
                conversation = await store.GetConversationAsync(userId, request.ConversationId);
                history = conversation?.Messages;
            }

            // Create new conversation if none exists
            if (conversation == null)
            {
                conversation = new AnalyticsConversation
                {
                    Database = request.Database,
                    UserId = userId,
                    Title = GenerateTitle(request.Question)
                };
            }

            var answer = await agent.AskAsync(request.Question, request.Database, history);

            // Append messages to conversation
            conversation.Messages.Add(new ChatEntry { Role = "user", Content = request.Question });
            conversation.Messages.Add(new ChatEntry { Role = "assistant", Content = answer });

            // Auto-title from first message if still default
            if (conversation.Title == "New Conversation" && conversation.Messages.Count >= 2)
                conversation.Title = GenerateTitle(request.Question);

            await store.SaveConversationAsync(conversation);

            return Results.Ok(new AnalyticsAskResponse
            {
                Answer = answer,
                ConversationId = conversation.Id
            });
        });
    }

    private static string GenerateTitle(string message)
    {
        var title = message.Trim();
        if (title.Length > 50)
            title = title[..47] + "...";
        return title;
    }

    private static List<string> ParseDatabaseNames(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return [];

            return data.EnumerateArray()
                .Where(r => r.TryGetProperty("name", out var v) && v.ValueKind == JsonValueKind.String)
                .Select(r => r.GetProperty("name").GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}

public record CreateConversationRequest(string Database);

public record AnalyticsAskRequest(string Question, string Database, string? ConversationId = null);

public record AnalyticsAskResponse
{
    public string Answer { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;
}
