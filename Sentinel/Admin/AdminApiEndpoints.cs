using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Sentinel.Admin.Auth;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;
using Sentinel.Infrastructure;

namespace Sentinel.Admin;

public static class AdminApiEndpoints
{
    public static void MapAdminApi(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization(AuthConstants.Policy);

        // ── Rules ──
        api.MapGet("/rules", async (IFeedbackRuleStore store) =>
            Results.Ok(await store.GetAllRulesAsync()));

        api.MapPost("/rules", async (FeedbackRule rule, IFeedbackRuleStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            rule.CreatedBy = ctx.User.Identity?.Name ?? "unknown";
            await store.SaveAsync(rule);
            await AuditAction(audit, ctx, "create", "rule", rule.Id, rule.Reason);
            return Results.Created($"/api/rules/{rule.Id}", rule);
        });

        api.MapPut("/rules/{id}", async (string id, FeedbackRule rule,
            IFeedbackRuleStore store, IAuditLogStore audit, HttpContext ctx) =>
        {
            rule.Id = id;
            await store.SaveAsync(rule);
            await AuditAction(audit, ctx, "update", "rule", id, rule.Reason);
            return Results.Ok(rule);
        });

        api.MapDelete("/rules/{id}", async (string id, IFeedbackRuleStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            await store.DeleteAsync(id);
            await AuditAction(audit, ctx, "delete", "rule", id);
            return Results.NoContent();
        });

        // ── Runs ──
        api.MapGet("/runs", async (IRunLogStore store, int? limit, int? offset) =>
            Results.Ok(await store.GetRecentRunsAsync(limit ?? 50, offset ?? 0)));

        api.MapGet("/runs/{runId}", async (string runId, IRunLogStore store) =>
        {
            var summary = await store.GetRunSummaryAsync(runId);
            var logs = await store.GetRunLogsAsync(runId);
            return Results.Ok(new { summary, logs });
        });

        api.MapPost("/runs/trigger", async (IServiceProvider sp, IAuditLogStore audit, HttpContext ctx) =>
        {
            var username = ctx.User.Identity?.Name ?? "unknown";
            Hangfire.BackgroundJob.Enqueue<Sentinel.Jobs.SentinelJob>(
                j => j.RunAsync($"manual:{username}"));
            await AuditAction(audit, ctx, "trigger_run", "run", "", $"Manual trigger by {username}");
            return Results.Accepted();
        });

        // ── Cases ──
        api.MapGet("/cases", async (Sentinel.Memory.ICaseStore store) =>
            Results.Ok(await store.GetOpenCasesAsync()));

        api.MapPost("/cases/{id}/feedback", async (string id, CaseFeedbackRequest req,
            Sentinel.Memory.ICaseStore caseStore, IFeedbackRuleStore ruleStore,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            switch (req.Action)
            {
                case "false_positive":
                    await caseStore.ResolveCaseAsync(id, $"False positive: {req.Reason}");
                    if (req.CreateRule != null)
                    {
                        req.CreateRule.SourceCaseId = id;
                        req.CreateRule.CreatedBy = ctx.User.Identity?.Name ?? "unknown";
                        await ruleStore.SaveAsync(req.CreateRule);
                    }
                    break;
                case "escalate":
                    var c = await caseStore.GetCaseAsync(id);
                    if (c != null) { c.Severity = "critical"; await caseStore.SaveCaseAsync(c); }
                    break;
                case "resolve":
                    await caseStore.ResolveCaseAsync(id, req.Reason ?? "Manually resolved");
                    break;
            }
            await AuditAction(audit, ctx, $"case_{req.Action}", "case", id, req.Reason);
            return Results.Ok();
        });

        // ── Users (admin only) ──
        var adminApi = api.MapGroup("/users").RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        adminApi.MapGet("/", async (IUserStore store) => Results.Ok(await store.GetAllAsync()));

        adminApi.MapPost("/", async (CreateUserRequest req, IUserStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            var user = new AdminUser
            {
                Username = req.Username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
                Role = req.Role,
                DisplayName = req.DisplayName,
                Email = req.Email
            };
            await store.SaveAsync(user);
            await AuditAction(audit, ctx, "create", "user", user.Id, user.Username);
            return Results.Created($"/api/users/{user.Id}", new { user.Id, user.Username, user.Role });
        });

        adminApi.MapDelete("/{id}", async (string id, IUserStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            await store.DeleteAsync(id);
            await AuditAction(audit, ctx, "delete", "user", id);
            return Results.NoContent();
        });

        // ── Audit ──
        api.MapGet("/audit", async (IAuditLogStore store, int? limit, int? offset,
            string? userId, string? action, string? resourceType) =>
            Results.Ok(await store.GetRecentAsync(limit ?? 100, offset ?? 0, userId, action, resourceType)));

        // ── Auth ──
        api.MapPost("/auth/login", async (LoginRequest req, IUserStore userStore,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            var user = await userStore.GetByUsernameAsync(req.Username);
            if (user == null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            {
                await AuditAction(audit, ctx, "login_failed", "auth", req.Username);
                return Results.Unauthorized();
            }

            await userStore.UpdateLastLoginAsync(user.Id);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id),
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.Role, user.Role),
                new("display_name", user.DisplayName)
            };
            var identity = new ClaimsIdentity(claims, AuthConstants.Scheme);
            var principal = new ClaimsPrincipal(identity);

            await ctx.SignInAsync(AuthConstants.Scheme, principal);
            await AuditAction(audit, ctx, "login", "auth", user.Id);
            return Results.Ok(new { user.Id, user.Username, user.Role, user.DisplayName });
        }).AllowAnonymous();

        api.MapPost("/auth/logout", async (HttpContext ctx, IAuditLogStore audit) =>
        {
            await AuditAction(audit, ctx, "logout", "auth", ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "");
            await ctx.SignOutAsync(AuthConstants.Scheme);
            return Results.Ok();
        });

        // ── System Prompt ──
        api.MapGet("/prompt", async (ISystemPromptStore store) =>
        {
            var current = await store.GetOverrideAsync();
            var history = await store.GetHistoryAsync();
            return Results.Ok(new { current, history });
        });

        api.MapPut("/prompt", async (PromptUpdateRequest req, ISystemPromptStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            await store.SaveOverrideAsync(req.PromptText, ctx.User.Identity?.Name ?? "unknown");
            await AuditAction(audit, ctx, "update", "prompt", "system_prompt", "Prompt updated");
            return Results.Ok();
        });

        api.MapDelete("/prompt", async (ISystemPromptStore store, IAuditLogStore audit, HttpContext ctx) =>
        {
            await store.ClearOverrideAsync();
            await AuditAction(audit, ctx, "reset", "prompt", "system_prompt", "Reverted to default");
            return Results.Ok();
        });

        // ── Analytics ──
        var analytics = api.MapGroup("/analytics");

        analytics.MapGet("/databases", async (ClickHouseClient ch) =>
        {
            var json = await ch.QueryAsync("SHOW DATABASES");
            var databases = ParseDatabaseNames(json)
                .Where(db => !SystemDatabases.Contains(db))
                .OrderBy(db => db)
                .ToList();
            return Results.Ok(databases);
        });

        analytics.MapGet("/conversations", async (IAnalyticsChatStore store, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "default";
            var conversations = await store.ListConversationsAsync(userId);
            return Results.Ok(conversations);
        });

        analytics.MapGet("/conversations/{id}", async (string id, IAnalyticsChatStore store, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "default";
            var conversation = await store.GetConversationAsync(userId, id);
            return conversation is null ? Results.NotFound() : Results.Ok(conversation);
        });

        analytics.MapPost("/conversations", async (CreateConversationRequest req, IAnalyticsChatStore store, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "default";
            var conversation = new AnalyticsConversation
            {
                Database = req.Database,
                UserId = userId
            };
            await store.SaveConversationAsync(conversation);
            return Results.Ok(conversation);
        });

        analytics.MapDelete("/conversations/{id}", async (string id, IAnalyticsChatStore store, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "default";
            await store.DeleteConversationAsync(userId, id);
            return Results.NoContent();
        });

        analytics.MapPost("/ask", async (AnalyticsAskRequest req, IAnalyticsJobStore jobStore,
            IAnalyticsChatStore chatStore, AnalyticsQueryWorker worker, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "default";
            var conversationId = req.ConversationId;

            if (string.IsNullOrEmpty(conversationId))
            {
                var conversation = new AnalyticsConversation
                {
                    Database = req.Database ?? "lipila_blaze",
                    UserId = userId,
                    Title = GenerateTitle(req.Prompt)
                };
                await chatStore.SaveConversationAsync(conversation);
                conversationId = conversation.Id;
            }

            var job = new AnalyticsQueryJob
            {
                ConversationId = conversationId,
                UserId = userId,
                Prompt = req.Prompt,
                Database = req.Database ?? "lipila_blaze"
            };

            await jobStore.CreateAsync(job);
            await worker.EnqueueAsync(job.Id);

            return Results.Ok(new { jobId = job.Id, conversationId, status = job.Status });
        });

        analytics.MapGet("/jobs/{jobId}", async (string jobId, IAnalyticsJobStore jobStore) =>
        {
            var job = await jobStore.GetAsync(jobId);
            if (job == null) return Results.NotFound();
            return Results.Ok(new
            {
                jobId = job.Id,
                status = job.Status,
                result = job.Result,
                error = job.Error,
                conversationId = job.ConversationId,
                submittedAt = job.SubmittedAt,
                completedAt = job.CompletedAt
            });
        });

        analytics.MapGet("/jobs", async (string? conversationId, IAnalyticsJobStore jobStore, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "default";
            var jobs = await jobStore.GetUserJobsAsync(userId, conversationId);
            return Results.Ok(jobs.Select(j => new
            {
                jobId = j.Id,
                status = j.Status,
                error = j.Error,
                conversationId = j.ConversationId,
                submittedAt = j.SubmittedAt,
                completedAt = j.CompletedAt
            }));
        });
    }

    private static readonly HashSet<string> SystemDatabases =
        ["system", "INFORMATION_SCHEMA", "information_schema", "default"];

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
        catch { return []; }
    }

    private static async Task AuditAction(IAuditLogStore audit, HttpContext ctx,
        string action, string resourceType, string? resourceId, string? details = null)
    {
        await audit.LogAsync(new AuditLog
        {
            UserId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "",
            Username = ctx.User.Identity?.Name ?? "anonymous",
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId ?? "",
            Details = details ?? "",
            IpAddress = ctx.Connection.RemoteIpAddress?.ToString() ?? ""
        });
    }
}

// Request DTOs
public record LoginRequest(string Username, string Password);
public record CreateUserRequest(string Username, string Password, string Role, string DisplayName, string? Email);
public record CaseFeedbackRequest(string Action, string? Reason, FeedbackRule? CreateRule);
public record PromptUpdateRequest(string PromptText);
public record AnalyticsRequest(string Prompt, string? Database);
public record AnalyticsAskRequest(string Prompt, string? Database, string? ConversationId = null);
public record CreateConversationRequest(string Database);
