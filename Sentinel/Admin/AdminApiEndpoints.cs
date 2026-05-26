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

        api.MapPost("/ask", async (AnalyticsAskRequest request, IAnalyticsJobStore jobStore, IAnalyticsChatStore chatStore, AnalyticsQueryWorker worker) =>
        {
            var userId = "default";
            var conversationId = request.ConversationId;

            // If no conversation exists, create one
            if (string.IsNullOrEmpty(conversationId))
            {
                var conversation = new AnalyticsConversation
                {
                    Database = request.Database,
                    UserId = userId,
                    Title = GenerateTitle(request.Question)
                };
                await chatStore.SaveConversationAsync(conversation);
                conversationId = conversation.Id;
            }

            var job = new AnalyticsQueryJob
            {
                ConversationId = conversationId,
                UserId = userId,
                Prompt = request.Question,
                Database = request.Database
            };

            await jobStore.CreateAsync(job);
            await worker.EnqueueAsync(job.Id);

            return Results.Ok(new AnalyticsAskResponse
            {
                JobId = job.Id,
                ConversationId = conversationId,
                Status = job.Status
            });
        });

        api.MapGet("/jobs/{jobId}", async (string jobId, IAnalyticsJobStore jobStore) =>
        {
            var job = await jobStore.GetAsync(jobId);
            if (job == null) return Results.NotFound();

            return Results.Ok(new JobStatusResponse
            {
                JobId = job.Id,
                Status = job.Status,
                Result = job.Result,
                Error = job.Error,
                ConversationId = job.ConversationId,
                SubmittedAt = job.SubmittedAt,
                CompletedAt = job.CompletedAt
            });
        });

        api.MapGet("/jobs", async (string? conversationId, IAnalyticsJobStore jobStore) =>
        {
            var userId = "default";
            var jobs = await jobStore.GetUserJobsAsync(userId, conversationId);
            return Results.Ok(jobs.Select(j => new JobStatusResponse
            {
                JobId = j.Id,
                Status = j.Status,
                Result = j.Result,
                Error = j.Error,
                ConversationId = j.ConversationId,
                SubmittedAt = j.SubmittedAt,
                CompletedAt = j.CompletedAt
            }));
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
    public string JobId { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

public record JobStatusResponse
{
    public string JobId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Result { get; init; }
    public string? Error { get; init; }
    public string ConversationId { get; init; } = string.Empty;
    public DateTime SubmittedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}
