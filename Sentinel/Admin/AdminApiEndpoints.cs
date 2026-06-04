using System.Security.Claims;
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
        app.MapPost("/admin/auth/signin", async (
            HttpContext ctx,
            IUserStore userStore,
            IAuditLogStore audit) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var identifier = form["username"].FirstOrDefault() ?? "";
            var password = form["password"].FirstOrDefault() ?? "";
            var returnUrl = form["returnUrl"].FirstOrDefault() ?? "/admin/chat";
            if (!returnUrl.StartsWith("/")) returnUrl = "/admin/chat";

            // Allow login with email or username
            var user = identifier.Contains('@')
                ? await userStore.GetByEmailAsync(identifier)
                : await userStore.GetByUsernameAsync(identifier);

            if (user == null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                await audit.LogAsync(new AuditLog
                {
                    Action = "login_failed", ResourceType = "auth",
                    ResourceId = identifier, Username = identifier,
                    IpAddress = ctx.Connection.RemoteIpAddress?.ToString() ?? ""
                });
                return Results.Redirect("/admin/login?error=invalid");
            }

            var claims = BuildClaims(user);
            await ctx.SignInAsync(AuthConstants.Scheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, AuthConstants.Scheme)));
            await userStore.UpdateLastLoginAsync(user.Id);
            await audit.LogAsync(new AuditLog
            {
                UserId = user.Id, Username = user.Username,
                Action = "login", ResourceType = "auth", ResourceId = user.Id,
                IpAddress = ctx.Connection.RemoteIpAddress?.ToString() ?? ""
            });

            return Results.Redirect(returnUrl);
        }).AllowAnonymous().DisableAntiforgery();

        app.MapPost("/admin/auth/signup", async (
            HttpContext ctx,
            IUserStore userStore,
            IAuditLogStore audit) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var email = (form["email"].FirstOrDefault() ?? "").Trim().ToLowerInvariant();
            var displayName = (form["displayName"].FirstOrDefault() ?? "").Trim();
            var password = form["password"].FirstOrDefault() ?? "";
            var confirm = form["confirmPassword"].FirstOrDefault() ?? "";

            if (!email.EndsWith("@hobbiton.co.zm"))
                return Results.Redirect("/admin/signup?error=domain");

            if (password.Length < 8)
                return Results.Redirect("/admin/signup?error=password");

            if (password != confirm)
                return Results.Redirect("/admin/signup?error=mismatch");

            var existing = await userStore.GetByEmailAsync(email);
            if (existing != null)
                return Results.Redirect("/admin/signup?error=exists");

            var username = email[..email.IndexOf('@')];
            // Ensure username is unique
            var takenUsernames = (await userStore.GetAllAsync()).Select(u => u.Username)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var finalUsername = username;
            var suffix = 1;
            while (takenUsernames.Contains(finalUsername))
                finalUsername = $"{username}{suffix++}";

            var user = new AdminUser
            {
                Username = finalUsername,
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? finalUsername : displayName,
                Role = AuthConstants.AnalystRole,
                IsActive = true
            };

            await userStore.SaveAsync(user);
            await audit.LogAsync(new AuditLog
            {
                UserId = user.Id, Username = user.Username,
                Action = "signup", ResourceType = "auth", ResourceId = user.Id,
                IpAddress = ctx.Connection.RemoteIpAddress?.ToString() ?? ""
            });

            var claims = BuildClaims(user);
            await ctx.SignInAsync(AuthConstants.Scheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, AuthConstants.Scheme)));

            return Results.Redirect("/admin/chat");
        }).AllowAnonymous().DisableAntiforgery();

        // ── Forgot password ──
        app.MapPost("/admin/auth/forgot-password", async (
            HttpContext ctx,
            IUserStore userStore,
            EmailClient emailClient) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var email = (form["email"].FirstOrDefault() ?? "").Trim().ToLowerInvariant();

            // Always redirect to the same page — never reveal whether the email exists
            if (!string.IsNullOrWhiteSpace(email))
            {
                var user = await userStore.GetByEmailAsync(email);
                if (user is { IsActive: true })
                {
                    var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                        .Replace("+", "-").Replace("/", "_").Replace("=", "");
                    user.PasswordResetToken = token;
                    user.PasswordResetTokenExpiry = DateTime.UtcNow.AddHours(1);
                    await userStore.SaveAsync(user);

                    var resetUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}/admin/reset-password?token={token}";
                    var body = $"""
                        Hi {user.DisplayName},

                        You requested a password reset for your Sentinel account.

                        **Reset your password** by visiting the link below (expires in 1 hour):

                        {resetUrl}

                        If you did not request this, you can safely ignore this email.
                        """;

                    await emailClient.SendAsync(
                        subject: "Reset your Sentinel password",
                        body: body,
                        severity: "info",
                        recipients: [user.Email!]);
                }
            }

            return Results.Redirect("/admin/forgot-password?sent=1");
        }).AllowAnonymous().DisableAntiforgery();

        // ── Reset password ──
        app.MapPost("/admin/auth/reset-password", async (
            HttpContext ctx,
            IUserStore userStore) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var token = form["token"].FirstOrDefault() ?? "";
            var password = form["password"].FirstOrDefault() ?? "";
            var confirm = form["confirmPassword"].FirstOrDefault() ?? "";

            if (string.IsNullOrWhiteSpace(token))
                return Results.Redirect("/admin/forgot-password");

            if (password.Length < 8)
                return Results.Redirect($"/admin/reset-password?token={token}&error=password");

            if (password != confirm)
                return Results.Redirect($"/admin/reset-password?token={token}&error=mismatch");

            var user = await userStore.GetByResetTokenAsync(token);
            if (user is null)
                return Results.Redirect("/admin/reset-password?error=expired");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
            user.PasswordResetToken = null;
            user.PasswordResetTokenExpiry = null;
            await userStore.SaveAsync(user);

            return Results.Redirect("/admin/login?reset=1");
        }).AllowAnonymous().DisableAntiforgery();

        var api = app.MapGroup("/api").RequireAuthorization(AuthConstants.Policy);

        api.MapGet("/rules", async (IFeedbackRuleStore store) =>
            Results.Ok(await store.GetAllRulesAsync()))
            .RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        api.MapPost("/rules", async (FeedbackRule rule, IFeedbackRuleStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            rule.CreatedBy = ctx.User.Identity?.Name ?? "unknown";
            await store.SaveAsync(rule);
            await AuditAction(audit, ctx, "create", "rule", rule.Id, rule.Reason);
            return Results.Created($"/api/rules/{rule.Id}", rule);
        }).RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        api.MapPut("/rules/{id}", async (string id, FeedbackRule rule,
            IFeedbackRuleStore store, IAuditLogStore audit, HttpContext ctx) =>
        {
            rule.Id = id;
            await store.SaveAsync(rule);
            await AuditAction(audit, ctx, "update", "rule", id, rule.Reason);
            return Results.Ok(rule);
        }).RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        api.MapDelete("/rules/{id}", async (string id, IFeedbackRuleStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            await store.DeleteAsync(id);
            await AuditAction(audit, ctx, "delete", "rule", id);
            return Results.NoContent();
        }).RequireAuthorization(AuthConstants.AdminOnlyPolicy);
        
        api.MapGet("/runs", async (IRunLogStore store, int? limit, int? offset) =>
            Results.Ok(await store.GetRecentRunsAsync(limit ?? 50, offset ?? 0)));

        api.MapGet("/runs/{runId}", async (string runId, IRunLogStore store) =>
        {
            var summary = await store.GetRunSummaryAsync(runId);
            var logs = await store.GetRunLogsAsync(runId);
            return Results.Ok(new { summary, logs });
        });

        api.MapPost("/runs/trigger", async (IAuditLogStore audit, HttpContext ctx) =>
        {
            var username = ctx.User.Identity?.Name ?? "unknown";
            Hangfire.BackgroundJob.Enqueue<Jobs.SentinelJob>(j =>
                j.RunAsync(new Sentinel.Agent.FraudAgentRunRequest { TriggeredBy = $"manual:{username}" }));
            await AuditAction(audit, ctx, "trigger_run", "run", "", $"Manual trigger by {username}");
            return Results.Accepted();
        });

        api.MapPost("/runs/{runId}/stop", async (string runId, RunCancellationRegistry cancellation,
            IActiveRunTracker runTracker, IAuditLogStore audit, HttpContext ctx) =>
        {
            var cancelled = cancellation.Cancel(runId);
            if (!cancelled)
            {
                // Try to mark as stopped even if CTS not found (may have already been removed)
                var state = await runTracker.GetAsync(runId);
                if (state is null) return Results.NotFound();
            }

            await runTracker.MarkStoppedAsync(runId);
            var username = ctx.User.Identity?.Name ?? "unknown";
            await AuditAction(audit, ctx, "stop_run", "run", runId, $"Stopped by {username}");
            return Results.Ok(new { stopped = true });
        });
        
        api.MapGet("/cases", async (Memory.ICaseStore store) =>
            Results.Ok(await store.GetOpenCasesAsync()))
            .RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        api.MapPost("/cases/{id}/feedback", async (string id, CaseFeedbackRequest req,
            Memory.ICaseStore caseStore, IFeedbackRuleStore ruleStore,
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
                    if (c != null)
                    {
                        c.Severity = "critical";
                        await caseStore.SaveCaseAsync(c);
                    }

                    break;
                case "resolve":
                    await caseStore.ResolveCaseAsync(id, req.Reason ?? "Manually resolved");
                    break;
            }

            await AuditAction(audit, ctx, $"case_{req.Action}", "case", id, req.Reason);
            return Results.Ok();
        }).RequireAuthorization(AuthConstants.AdminOnlyPolicy);

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
            Results.Ok(await store.GetRecentAsync(limit ?? 100, offset ?? 0, userId, action, resourceType)))
            .RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        // ── Auth ──
        api.MapPost("/auth/login", async (LoginRequest req, IUserStore userStore,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            var user = req.Username.Contains('@')
                ? await userStore.GetByEmailAsync(req.Username)
                : await userStore.GetByUsernameAsync(req.Username);

            if (user == null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            {
                await AuditAction(audit, ctx, "login_failed", "auth", req.Username);
                return Results.Unauthorized();
            }

            await userStore.UpdateLastLoginAsync(user.Id);
            var claims = BuildClaims(user);
            await ctx.SignInAsync(AuthConstants.Scheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, AuthConstants.Scheme)));
            await AuditAction(audit, ctx, "login", "auth", user.Id);
            return Results.Ok(new { user.Id, user.Username, user.Role, user.DisplayName });
        }).AllowAnonymous();

        api.MapPost("/auth/logout", async (HttpContext ctx, IAuditLogStore audit) =>
        {
            var returnUrl = ctx.Request.Query["returnUrl"].FirstOrDefault()
                            ?? (ctx.Request.HasFormContentType
                                ? (await ctx.Request.ReadFormAsync())["returnUrl"].FirstOrDefault()
                                : null)
                            ?? "/admin";
            if (!returnUrl.StartsWith("/")) returnUrl = "/admin";

            await AuditAction(audit, ctx, "logout", "auth", ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "");
            await ctx.SignOutAsync(AuthConstants.Scheme);
            return Results.Redirect(returnUrl);
        });

        // ── Analytics ──
        var analytics = api.MapGroup("/analytics");

        analytics.MapGet("/databases", async (SchemaLoader schemaLoader) =>
        {
            var databases = await schemaLoader.GetDatabasesAsync();
            return Results.Ok(databases.OrderBy(db => db).ToList());
        });

        analytics.MapPost("/schema/refresh", async (SchemaLoader schemaLoader) =>
        {
            await schemaLoader.InvalidateAllAsync();
            await schemaLoader.WarmAllAsync();
            return Results.Ok(new { message = "Schema cache refreshed" });
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

        analytics.MapPost("/conversations",
            async (CreateConversationRequest req, IAnalyticsChatStore store, HttpContext ctx) =>
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

        // ── Fraud Patterns (Settings) ──
        var patterns = api.MapGroup("/patterns").RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        patterns.MapGet("/", async (IFraudPatternStore store) =>
            Results.Ok(await store.GetAllAsync()));

        patterns.MapGet("/{id:int}", async (int id, IFraudPatternStore store) =>
        {
            var pattern = await store.GetByIdAsync(id);
            return pattern is null ? Results.NotFound() : Results.Ok(pattern);
        });

        patterns.MapPost("/", async (FraudPatternEntity pattern, IFraudPatternStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            pattern.CreatedBy = ctx.User.Identity?.Name ?? "unknown";
            pattern.CreatedAt = DateTime.UtcNow;
            await store.UpsertAsync(pattern);
            await AuditAction(audit, ctx, "create", "fraud_pattern", pattern.Id.ToString(), pattern.Name);
            return Results.Created($"/api/patterns/{pattern.Id}", pattern);
        });

        patterns.MapPut("/{id:int}", async (int id, FraudPatternEntity pattern, IFraudPatternStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            pattern.Id = id;
            pattern.CreatedBy = ctx.User.Identity?.Name ?? "unknown";
            await store.UpsertAsync(pattern);
            await AuditAction(audit, ctx, "update", "fraud_pattern", id.ToString(), pattern.Name);
            return Results.Ok(pattern);
        });

        patterns.MapDelete("/{id:int}", async (int id, IFraudPatternStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            await store.DeleteAsync(id);
            await AuditAction(audit, ctx, "delete", "fraud_pattern", id.ToString());
            return Results.NoContent();
        });

        // ── Evidence Sources CRUD ──
        var evidence = api.MapGroup("/evidence-sources").RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        evidence.MapGet("/", async (IEvidenceSourceStore store) =>
            Results.Ok(await store.GetAllAsync()));

        evidence.MapGet("/{id:int}", async (int id, IEvidenceSourceStore store) =>
        {
            var source = await store.GetByIdAsync(id);
            return source is null ? Results.NotFound() : Results.Ok(source);
        });

        evidence.MapPost("/", async (EvidenceSource source, IEvidenceSourceStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            source.CreatedAt = DateTime.UtcNow;
            source.UpdatedAt = DateTime.UtcNow;
            await store.UpsertAsync(source);
            await AuditAction(audit, ctx, "create", "evidence_source", source.Id.ToString(), source.Name);
            return Results.Created($"/api/evidence-sources/{source.Id}", source);
        });

        evidence.MapPut("/{id:int}", async (int id, EvidenceSource source, IEvidenceSourceStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            source.Id = id;
            source.UpdatedAt = DateTime.UtcNow;
            await store.UpsertAsync(source);
            await AuditAction(audit, ctx, "update", "evidence_source", id.ToString(), source.Name);
            return Results.Ok(source);
        });

        evidence.MapDelete("/{id:int}", async (int id, IEvidenceSourceStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            await store.DeleteAsync(id);
            await AuditAction(audit, ctx, "delete", "evidence_source", id.ToString());
            return Results.NoContent();
        });
    }

    private static List<Claim> BuildClaims(AdminUser user) =>
    [
        new(ClaimTypes.NameIdentifier, user.Id),
        new(ClaimTypes.Name, user.Username),
        new(ClaimTypes.Role, user.Role),
        new("display_name", user.DisplayName)
    ];

    private static string GenerateTitle(string message)
    {
        var title = message.Trim();
        if (title.Length > 50)
            title = title[..47] + "...";
        return title;
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

public record LoginRequest(string Username, string Password);

public record CreateUserRequest(string Username, string Password, string Role, string DisplayName, string? Email);

public record CaseFeedbackRequest(string Action, string? Reason, FeedbackRule? CreateRule);

public record AnalyticsAskRequest(string Prompt, string? Database, string? ConversationId = null);

public record CreateConversationRequest(string Database);