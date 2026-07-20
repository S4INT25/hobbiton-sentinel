using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Security.Cryptography;
using Hangfire;
using Microsoft.AspNetCore.Authentication;
using Sentinel.Admin.Auth;
using Sentinel.Admin.Models;
using Sentinel.Admin.Stores;
using Sentinel.Agent;
using Sentinel.Infrastructure;
using Sentinel.Jobs;
using Sentinel.Memory;
using ZiggyCreatures.Caching.Fusion;

namespace Sentinel.Admin;

public static class AdminApiEndpoints
{
    [Experimental("OPENAI001")]
    public static void MapAdminApi(this WebApplication app)
    {
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
            BackgroundJob.Enqueue<SentinelJob>(j =>
                j.RunAsync(new FraudAgentRunRequest { TriggeredBy = $"manual:{username}" }));
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

        api.MapGet("/cases", async (ICaseStore store) =>
                Results.Ok(await store.GetOpenCasesAsync()))
            .RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        api.MapPost("/cases/{id}/feedback", async (string id, CaseFeedbackRequest req,
            ICaseStore caseStore, IFeedbackRuleStore ruleStore, ICaseOutcomeStore outcomeStore,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            var username = ctx.User.Identity?.Name ?? "analyst";
            var existing = await caseStore.GetCaseAsync(id);

            // Record a historical outcome so the agent learns from analyst decisions
            async Task RecordOutcome(string outcome, string resolution)
            {
                if (existing is null) return;
                await outcomeStore.SaveAsync(new CaseOutcome
                {
                    CaseId = id,
                    Title = existing.Title,
                    Category = existing.Category,
                    PatternId = existing.PatternId,
                    Outcome = outcome,
                    OriginalSeverity = existing.Severity,
                    Confidence = existing.Confidence,
                    AffectedEntities = string.Join(", ", existing.AffectedEntities),
                    WorkflowId = existing.WorkflowId,
                    Resolution = resolution,
                    ResolvedBy = username,
                    OccurrenceCount = existing.OccurrenceCount
                });
            }

            switch (req.Action)
            {
                case "false_positive":
                    await RecordOutcome("false_positive", $"False positive: {req.Reason}");
                    await caseStore.ResolveCaseAsync(id, $"False positive: {req.Reason}");
                    if (req.CreateRule != null)
                    {
                        req.CreateRule.SourceCaseId = id;
                        req.CreateRule.CreatedBy = ctx.User.Identity?.Name ?? "unknown";
                        await ruleStore.SaveAsync(req.CreateRule);
                    }

                    break;
                case "escalate":
                    if (existing != null)
                    {
                        existing.Severity = "critical";
                        await caseStore.SaveCaseAsync(existing);
                    }

                    break;
                case "resolve":
                    await RecordOutcome("confirmed_fraud", req.Reason ?? "Manually resolved by analyst");
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
            IAuditLogStore audit, IFusionCache cache, HttpContext ctx) =>
        {
            var user = req.Username.Contains('@')
                ? await userStore.GetByEmailAsync(req.Username)
                : await userStore.GetByUsernameAsync(req.Username);

            if (user == null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            {
                await AuditAction(audit, ctx, "login_failed", "auth", req.Username);
                return Results.Unauthorized();
            }

            if (!user.EmailVerified)
                return Results.BadRequest(new
                {
                    error = "Please verify your email before signing in.",
                    verificationRequired = true,
                    email = user.Email
                });

            return await CompletePrimaryLoginAsync(user, userStore, cache, audit, ctx);
        }).AllowAnonymous();

        // Passwordless alternative to /auth/login — same downstream TOTP 2FA gate via CompletePrimaryLoginAsync.
        api.MapPost("/auth/login/email-otp/request", async (RequestLoginOtpRequest req, IUserStore userStore,
            EmailClient emailClient) =>
        {
            var email = req.Email.Trim().ToLowerInvariant();
            var user = await userStore.GetByEmailAsync(email);
            // Always 200 — don't reveal whether the email exists, is verified, or is active.
            if (user is { IsActive: true, EmailVerified: true } && CanIssueNewOtp(user))
            {
                SetEmailOtp(user);
                await userStore.SaveAsync(user);
                await SendEmailOtp(emailClient, user, "Your Sentinel sign-in code",
                    $"Your sign-in code is **{user.EmailOtpCode}**. It expires in 10 minutes.\n\n" +
                    "If you did not request this, ignore this email.");
            }

            return Results.Ok(new { sent = true });
        }).AllowAnonymous();

        api.MapPost("/auth/login/email-otp/verify", async (VerifyLoginOtpRequest req, IUserStore userStore,
            IAuditLogStore audit, IFusionCache cache, HttpContext ctx) =>
        {
            var email = req.Email.Trim().ToLowerInvariant();
            var user = await userStore.GetByEmailAsync(email);

            if (user is null || !user.IsActive || !user.EmailVerified || user.EmailOtpCode is null
                || user.EmailOtpCodeExpiry is null || user.EmailOtpCodeExpiry < DateTime.UtcNow
                || user.EmailOtpAttempts >= 5)
            {
                await AuditAction(audit, ctx, "login_failed", "auth", email);
                return Results.BadRequest(new { error = "This code has expired. Request a new one." });
            }

            if (user.EmailOtpCode != req.Code.Trim())
            {
                user.EmailOtpAttempts++;
                await userStore.SaveAsync(user);
                await AuditAction(audit, ctx, "login_failed", "auth", email);
                return Results.BadRequest(new { error = "Incorrect code." });
            }

            user.EmailOtpCode = null;
            user.EmailOtpCodeExpiry = null;
            await userStore.SaveAsync(user);

            return await CompletePrimaryLoginAsync(user, userStore, cache, audit, ctx);
        }).AllowAnonymous();

        // Completes login after /auth/login returned twoFactorRequired.
        api.MapPost("/auth/login/2fa", async (TwoFactorLoginRequest req, IUserStore userStore,
            IAuditLogStore audit, IFusionCache cache, HttpContext ctx) =>
        {
            var key = TwoFactorChallengeKey(req.Challenge);
            var pending = await cache.GetOrDefaultAsync<TwoFactorChallenge>(key);
            var user = pending is null ? null : await userStore.GetByIdAsync(pending.UserId);

            if (pending is null || user is null || string.IsNullOrEmpty(user.TwoFactorSecret))
                return Results.BadRequest(new { error = "This code has expired. Please sign in again." });

            if (!TwoFactorCodes.VerifyTotp(user.TwoFactorSecret, req.Code))
            {
                // ponytail: attempt cap resets the 5-min TTL rather than tracking absolute expiry — fine at this scale
                if (pending.Attempts + 1 >= 5) await cache.RemoveAsync(key);
                else
                    await cache.SetAsync(key, pending with { Attempts = pending.Attempts + 1 },
                        o => o.SetDuration(TimeSpan.FromMinutes(5)));
                return Results.BadRequest(new { error = "Incorrect code." });
            }

            await cache.RemoveAsync(key);
            return await FinishSignInAsync(user, userStore, audit, ctx);
        }).AllowAnonymous();

        api.MapPost("/auth/logout", async (HttpContext ctx, IAuditLogStore audit) =>
        {
            var returnUrl = ctx.Request.Query["returnUrl"].FirstOrDefault()
                            ?? (ctx.Request.HasFormContentType
                                ? (await ctx.Request.ReadFormAsync())["returnUrl"].FirstOrDefault()
                                : null)
                            ?? "/admin/chat";
            if (!returnUrl.StartsWith("/")) returnUrl = "/admin/chat";

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
                Database = req.Database ?? "lipila_blaze",
                Mode = req.Mode ?? "general"
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
                completedAt = job.CompletedAt,
                streamEvents = job.StreamEvents
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

        // ── JSON auth (SPA) ──
        api.MapPost("/auth/signup", async (SignupRequest req, IUserStore userStore,
            EmailClient emailClient, IAuditLogStore audit, HttpContext ctx) =>
        {
            var email = req.Email.Trim().ToLowerInvariant();
            if (!email.EndsWith("@hobbiton.co.zm"))
                return Results.BadRequest(new { error = "Only @hobbiton.co.zm emails are allowed." });
            if (req.Password.Length < 8)
                return Results.BadRequest(new { error = "Password must be at least 8 characters." });
            if (req.Password != req.ConfirmPassword)
                return Results.BadRequest(new { error = "Passwords do not match." });
            if (await userStore.GetByEmailAsync(email) != null)
                return Results.BadRequest(new { error = "An account with this email already exists." });

            var username = email[..email.IndexOf('@')];
            var taken = (await userStore.GetAllAsync()).Select(u => u.Username)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var finalUsername = username;
            var suffix = 1;
            while (taken.Contains(finalUsername)) finalUsername = $"{username}{suffix++}";

            var user = new AdminUser
            {
                Username = finalUsername,
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
                DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? finalUsername : req.DisplayName,
                Role = AuthConstants.AnalystRole,
                IsActive = true,
                EmailVerified = false
            };
            SetEmailOtp(user);
            await userStore.SaveAsync(user);
            await SendEmailOtp(emailClient, user, "Verify your Sentinel account",
                $"Hi {user.DisplayName},\n\nYour verification code is **{user.EmailOtpCode}**. " +
                "It expires in 10 minutes.\n\nIf you did not create a Sentinel account, ignore this email.");
            await AuditAction(audit, ctx, "signup", "auth", user.Id);
            return Results.Ok(new { verificationRequired = true, email = user.Email });
        }).AllowAnonymous();

        // Second factor for registration — proves the signup email is real before first login.
        api.MapPost("/auth/verify-email", async (VerifyEmailRequest req, IUserStore userStore,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            var email = req.Email.Trim().ToLowerInvariant();
            var user = await userStore.GetByEmailAsync(email);
            if (user is null || user.EmailVerified)
                return Results.BadRequest(new { error = "Invalid or expired code." });

            if (user.EmailOtpCodeExpiry is null || user.EmailOtpCodeExpiry < DateTime.UtcNow
                                                || user.EmailOtpAttempts >= 5)
                return Results.BadRequest(new { error = "This code has expired. Request a new one." });

            if (user.EmailOtpCode != req.Code.Trim())
            {
                user.EmailOtpAttempts++;
                await userStore.SaveAsync(user);
                return Results.BadRequest(new { error = "Incorrect code." });
            }

            user.EmailVerified = true;
            user.EmailOtpCode = null;
            user.EmailOtpCodeExpiry = null;
            await userStore.SaveAsync(user);

            var principal = new ClaimsPrincipal(new ClaimsIdentity(BuildClaims(user), AuthConstants.Scheme));
            await ctx.SignInAsync(AuthConstants.Scheme, principal);
            ctx.User = principal;
            await AuditAction(audit, ctx, "verify_email", "auth", user.Id);
            return Results.Ok(new { user.Id, user.Username, user.Role, user.DisplayName });
        }).AllowAnonymous();

        api.MapPost("/auth/resend-verification", async (ResendVerificationRequest req, IUserStore userStore,
            EmailClient emailClient) =>
        {
            var email = req.Email.Trim().ToLowerInvariant();
            var user = await userStore.GetByEmailAsync(email);
            // Always 200 — don't reveal whether the email exists. Skip if a code was just sent (anti-spam).
            if (user is { EmailVerified: false } && CanIssueNewOtp(user))
            {
                SetEmailOtp(user);
                await userStore.SaveAsync(user);
                await SendEmailOtp(emailClient, user, "Verify your Sentinel account",
                    $"Hi {user.DisplayName},\n\nYour verification code is **{user.EmailOtpCode}**. " +
                    "It expires in 10 minutes.\n\nIf you did not create a Sentinel account, ignore this email.");
            }

            return Results.Ok(new { sent = true });
        }).AllowAnonymous();

        api.MapPost("/auth/forgot-password", async (ForgotPasswordRequest req, IUserStore userStore,
            EmailClient emailClient, HttpContext ctx) =>
        {
            var email = req.Email.Trim().ToLowerInvariant();
            // Always 200 — never reveal whether the email exists
            var user = await userStore.GetByEmailAsync(email);
            if (user is { IsActive: true })
            {
                var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                    .Replace("+", "-").Replace("/", "_").Replace("=", "");
                user.PasswordResetToken = token;
                user.PasswordResetTokenExpiry = DateTime.UtcNow.AddHours(1);
                await userStore.SaveAsync(user);

                var resetUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}/reset-password?token={token}";
                await emailClient.SendAsync(
                    subject: "Reset your Sentinel password",
                    body:
                    $"Hi {user.DisplayName},\n\nReset your Sentinel password via the link below (expires in 1 hour):\n\n{resetUrl}\n\nIf you did not request this, ignore this email.",
                    severity: "info",
                    recipients: [user.Email!]);
            }

            return Results.Ok(new { sent = true });
        }).AllowAnonymous();

        api.MapPost("/auth/reset-password", async (ResetPasswordRequest req, IUserStore userStore) =>
        {
            if (req.Password.Length < 8)
                return Results.BadRequest(new { error = "Password must be at least 8 characters." });
            if (req.Password != req.ConfirmPassword)
                return Results.BadRequest(new { error = "Passwords do not match." });
            var user = await userStore.GetByResetTokenAsync(req.Token);
            if (user is null)
                return Results.BadRequest(new { error = "This reset link is invalid or has expired." });
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);
            user.PasswordResetToken = null;
            user.PasswordResetTokenExpiry = null;
            await userStore.SaveAsync(user);
            return Results.Ok(new { reset = true });
        }).AllowAnonymous();

        // ── Session ──
        api.MapGet("/auth/me", (HttpContext ctx) => Results.Ok(new
        {
            Id = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "",
            Username = ctx.User.Identity?.Name ?? "",
            Role = ctx.User.FindFirst(ClaimTypes.Role)?.Value ?? "",
            DisplayName = ctx.User.FindFirst("display_name")?.Value ?? ""
        }));

        // ── Two-factor authentication (self-service, acts on the caller's own account) ──
        api.MapGet("/auth/2fa/status", async (IUserStore userStore, HttpContext ctx) =>
        {
            var user = await CurrentUser(userStore, ctx);
            return user is null ? Results.NotFound() : Results.Ok(new { enabled = user.TwoFactorEnabled });
        });

        api.MapPost("/auth/2fa/setup", async (IUserStore userStore, HttpContext ctx) =>
        {
            var user = await CurrentUser(userStore, ctx);
            if (user is null) return Results.NotFound();

            var secret = TwoFactorCodes.GenerateSecret();
            user.TwoFactorSecret = secret;
            await userStore.SaveAsync(user);
            return Results.Ok(new
            {
                secret,
                otpauthUrl = TwoFactorCodes.BuildOtpauthUrl(secret, user.Email ?? user.Username)
            });
        });

        api.MapPost("/auth/2fa/enable", async (TwoFactorCodeRequest req, IUserStore userStore,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            var user = await CurrentUser(userStore, ctx);
            if (user is null) return Results.NotFound();
            if (string.IsNullOrEmpty(user.TwoFactorSecret) ||
                !TwoFactorCodes.VerifyTotp(user.TwoFactorSecret, req.Code))
                return Results.BadRequest(new { error = "Incorrect code." });

            user.TwoFactorEnabled = true;
            await userStore.SaveAsync(user);
            await AuditAction(audit, ctx, "enable_2fa", "auth", user.Id);
            return Results.Ok(new { enabled = true });
        });

        api.MapPost("/auth/2fa/disable", async (DisableTwoFactorRequest req, IUserStore userStore,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            var user = await CurrentUser(userStore, ctx);
            if (user is null) return Results.NotFound();
            if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
                return Results.BadRequest(new { error = "Incorrect password." });

            user.TwoFactorEnabled = false;
            user.TwoFactorSecret = null;
            await userStore.SaveAsync(user);
            await AuditAction(audit, ctx, "disable_2fa", "auth", user.Id);
            return Results.Ok(new { enabled = false });
        });

        // ── Cases (detail / delete / bulk) ──
        api.MapGet("/cases/{id}", async (string id, ICaseStore store) =>
        {
            var c = await store.GetCaseAsync(id);
            return c is null ? Results.NotFound() : Results.Ok(c);
        }).RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        api.MapDelete("/cases/{id}", async (string id, ICaseStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            await store.DeleteCaseAsync(id);
            await AuditAction(audit, ctx, "delete", "case", id);
            return Results.NoContent();
        }).RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        api.MapPost("/cases/bulk-resolve", async (BulkResolveRequest req, ICaseStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            var count = await store.ResolveCasesAsync(req.Ids, req.Resolution);
            await AuditAction(audit, ctx, "bulk_case_resolve", "case",
                string.Join(",", req.Ids), $"Bulk resolved {count} case(s): {req.Resolution}");
            return Results.Ok(new { count });
        }).RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        api.MapGet("/cases/{id}/related-outcomes", async (string id, ICaseStore caseStore,
            ICaseOutcomeStore outcomes) =>
        {
            var c = await caseStore.GetCaseAsync(id);
            if (c is null) return Results.NotFound();
            var related = await outcomes.FindByEntitiesAsync(c.AffectedEntities);
            return Results.Ok(related);
        }).RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        api.MapGet("/outcome-stats", async (ICaseOutcomeStore outcomes, string? database) =>
                Results.Ok(await outcomes.GetStatsAsync(database)))
            .RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        // ── Workflows ──
        var workflows = api.MapGroup("/workflows");

        workflows.MapGet("/", async (IWorkflowStore store) => Results.Ok(await store.GetAllAsync()));

        workflows.MapGet("/{id}", async (string id, IWorkflowStore store) =>
        {
            var wf = await store.GetByIdAsync(id);
            return wf is null ? Results.NotFound() : Results.Ok(wf);
        });

        workflows.MapPost("/", async (WorkflowDefinition wf, IWorkflowStore store,
            WorkflowSchedulerService scheduler, IAuditLogStore audit, HttpContext ctx) =>
        {
            await store.UpsertAsync(wf);
            await scheduler.RefreshSchedulesAsync();
            await AuditAction(audit, ctx, "create", "workflow", wf.Id, wf.Name);
            return Results.Created($"/api/workflows/{wf.Id}", wf);
        }).RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        workflows.MapPut("/{id}", async (string id, WorkflowDefinition wf, IWorkflowStore store,
            WorkflowSchedulerService scheduler, IAuditLogStore audit, HttpContext ctx) =>
        {
            wf.Id = id;
            await store.UpsertAsync(wf);
            await scheduler.RefreshSchedulesAsync();
            await AuditAction(audit, ctx, "update", "workflow", id, wf.Name);
            return Results.Ok(wf);
        }).RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        workflows.MapDelete("/{id}", async (string id, IWorkflowStore store,
            WorkflowSchedulerService scheduler, IAuditLogStore audit, HttpContext ctx) =>
        {
            await store.DeleteAsync(id);
            scheduler.RemoveWorkflowSchedule(id);
            await AuditAction(audit, ctx, "delete", "workflow", id);
            return Results.NoContent();
        }).RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        workflows.MapPost("/{id}/trigger", async (string id, IWorkflowStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            var wf = await store.GetByIdAsync(id);
            if (wf is null) return Results.NotFound();
            BackgroundJob.Enqueue<WorkflowExecutionJob>(j => j.ExecuteAsync(id));
            await AuditAction(audit, ctx, "trigger", "workflow", id, wf.Name);
            return Results.Accepted();
        }).RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        workflows.MapGet("/{id}/runs", async (string id, IRunLogStore store) =>
        {
            var runs = await store.GetRecentRunsAsync(200);
            return Results.Ok(runs
                .Where(r => r.TriggeredBy.Equals($"workflow:{id}", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.StartedAt)
                .ToList());
        });

        workflows.MapGet("/{id}/patterns", async (string id, IFraudPatternStore store) =>
            Results.Ok(await store.GetByWorkflowAsync(id)));

        workflows.MapGet("/{id}/evidence-sources", async (string id, IEvidenceSourceStore store) =>
            Results.Ok(await store.GetByWorkflowAsync(id)));

        // ── Active runs ──
        api.MapGet("/runs/active", async (IActiveRunTracker tracker) =>
            Results.Ok(await tracker.GetActiveRunsAsync()));

        // ── Knowledge base (agent memories) ──
        var knowledge = api.MapGroup("/knowledge").RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        knowledge.MapGet("/", async (IAgentMemoryStore store) => Results.Ok(await store.GetAllAsync()));

        knowledge.MapPost("/", async (AgentMemory memory, IAgentMemoryStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            memory.CreatedBy = ctx.User.Identity?.Name ?? "admin";
            await store.SaveAsync(memory);
            await AuditAction(audit, ctx, memory.Id == 0 ? "create" : "update", "knowledge",
                memory.Id.ToString(), memory.Term);
            return Results.Ok(memory);
        });

        knowledge.MapDelete("/{id:int}", async (int id, IAgentMemoryStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            await store.DeleteAsync(id);
            await AuditAction(audit, ctx, "delete", "knowledge", id.ToString());
            return Results.NoContent();
        });

        // ── Database products ──
        api.MapGet("/products/enabled", async (IDatabaseProductStore store) =>
            Results.Ok(await store.GetEnabledAsync()));

        var products = api.MapGroup("/products").RequireAuthorization(AuthConstants.AdminOnlyPolicy);

        products.MapGet("/", async (IDatabaseProductStore store) => Results.Ok(await store.GetAllAsync()));

        products.MapPost("/", async (DatabaseProduct product, IDatabaseProductStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            await store.UpsertAsync(product);
            await AuditAction(audit, ctx, "upsert", "product", product.Id.ToString(), product.DisplayName);
            return Results.Ok(product);
        });

        products.MapDelete("/{id:int}", async (int id, IDatabaseProductStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            await store.DeleteAsync(id);
            await AuditAction(audit, ctx, "delete", "product", id.ToString());
            return Results.NoContent();
        });

        // ── Users (update) ──
        adminApi.MapPut("/{id}", async (string id, UpdateUserRequest req, IUserStore store,
            IAuditLogStore audit, HttpContext ctx) =>
        {
            var user = await store.GetByIdAsync(id);
            if (user is null) return Results.NotFound();
            if (req.Role is not null) user.Role = req.Role;
            if (req.DisplayName is not null) user.DisplayName = req.DisplayName;
            if (req.IsActive is not null) user.IsActive = req.IsActive.Value;
            if (!string.IsNullOrWhiteSpace(req.Password))
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);
            await store.SaveAsync(user);
            await AuditAction(audit, ctx, "update", "user", id, user.Username);
            return Results.Ok(new { user.Id, user.Username, user.Role, user.DisplayName, user.IsActive });
        });

        // ── Conversations (rename / share) ──
        analytics.MapPut("/conversations/{id}", async (string id, RenameConversationRequest req,
            IAnalyticsChatStore store, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "default";
            var conv = await store.GetConversationAsync(userId, id);
            if (conv is null) return Results.NotFound();
            conv.Title = req.Title;
            await store.SaveConversationAsync(conv);
            return Results.Ok(conv);
        });

        analytics.MapPost("/conversations/{id}/share", async (string id,
            IAnalyticsChatStore store, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "default";
            var conv = await store.GetConversationAsync(userId, id);
            if (conv is null) return Results.NotFound();
            await store.ShareConversationAsync(conv);
            return Results.Ok(new { shareId = id });
        });

        analytics.MapDelete("/conversations/{id}/share", async (string id, IAnalyticsChatStore store) =>
        {
            await store.UnshareConversationAsync(id);
            return Results.NoContent();
        });

        analytics.MapGet("/tables", async (string database, ClickHouseClient clickHouse) =>
        {
            var escapedDb = database.Replace("`", "");
            var json = await clickHouse.QueryAsync($"SHOW TABLES FROM `{escapedDb}`");
            return Results.Content(json, "application/json");
        });

        // ── Shared reports (public) ──
        app.MapGet("/api/shared/{id}", async (string id, IAnalyticsChatStore store) =>
        {
            var conv = await store.GetSharedConversationAsync(id);
            return conv is null ? Results.NotFound() : Results.Ok(conv);
        }).AllowAnonymous();

        // ── Dashboard ──
        api.MapGet("/dashboard", async (ICaseStore caseStore, IRunLogStore runStore,
            IFeedbackRuleStore ruleStore, IWorkflowStore workflowStore, IActiveRunTracker tracker) =>
        {
            var cases = await caseStore.GetOpenCasesAsync();
            var rules = await ruleStore.GetActiveRulesAsync();
            var runs = await runStore.GetRecentRunsAsync(100);
            var activeRuns = await tracker.GetActiveRunsAsync();
            var allWorkflows = await workflowStore.GetAllAsync();
            return Results.Ok(new { cases, rules, runs, activeRuns, workflows = allWorkflows });
        }).RequireAuthorization(AuthConstants.AdminOrDeveloperPolicy);
    }

    private static List<Claim> BuildClaims(AdminUser user) =>
    [
        new(ClaimTypes.NameIdentifier, user.Id),
        new(ClaimTypes.Name, user.Username),
        new(ClaimTypes.Role, user.Role),
        new("display_name", user.DisplayName)
    ];

    private static void SetEmailOtp(AdminUser user)
    {
        user.EmailOtpCode = TwoFactorCodes.GenerateEmailCode();
        user.EmailOtpCodeExpiry = DateTime.UtcNow.AddMinutes(10);
        user.EmailOtpAttempts = 0;
    }

    // A pending code less than a minute old is still in the recipient's inbox — don't re-send yet.
    private static bool CanIssueNewOtp(AdminUser user) =>
        user.EmailOtpCodeExpiry is null || user.EmailOtpCodeExpiry < DateTime.UtcNow.AddMinutes(9);

    private static Task SendEmailOtp(EmailClient emailClient, AdminUser user, string subject, string body) =>
        emailClient.SendAsync(subject: subject, body: body, severity: "info", recipients: [user.Email!]);

    // Shared tail for both primary-login paths (password, email-OTP): either hand off to TOTP or sign in.
    private static async Task<IResult> CompletePrimaryLoginAsync(AdminUser user, IUserStore userStore,
        IFusionCache cache, IAuditLogStore audit, HttpContext ctx)
    {
        if (user.TwoFactorEnabled)
        {
            var challenge = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace("+", "-").Replace("/", "_").Replace("=", "");
            await cache.SetAsync(TwoFactorChallengeKey(challenge), new TwoFactorChallenge(user.Id, 0),
                o => o.SetDuration(TimeSpan.FromMinutes(5)));
            return Results.Ok(new { twoFactorRequired = true, challenge });
        }

        return await FinishSignInAsync(user, userStore, audit, ctx);
    }

    private static async Task<IResult> FinishSignInAsync(AdminUser user, IUserStore userStore,
        IAuditLogStore audit, HttpContext ctx)
    {
        await userStore.UpdateLastLoginAsync(user.Id);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(BuildClaims(user), AuthConstants.Scheme));
        await ctx.SignInAsync(AuthConstants.Scheme, principal);
        ctx.User = principal;
        await AuditAction(audit, ctx, "login", "auth", user.Id);
        return Results.Ok(new { user.Id, user.Username, user.Role, user.DisplayName });
    }

    private static async Task<AdminUser?> CurrentUser(IUserStore userStore, HttpContext ctx)
    {
        var id = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return id is null ? null : await userStore.GetByIdAsync(id);
    }

    private static string TwoFactorChallengeKey(string token) => $"sentinel:2fa-challenge:{token}";

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
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
        if (ip == "127.0.0.1" || ip == "::1" || ip == "0.0.0.1")
        {
            ip = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                 ?? ctx.Request.Headers["X-Real-IP"].FirstOrDefault()
                 ?? ip;
        }

        await audit.LogAsync(new AuditLog
        {
            UserId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "",
            Username = ctx.User.Identity?.Name ?? "anonymous",
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId ?? "",
            Details = details ?? "",
            IpAddress = ip
        });
    }
}

public record LoginRequest(string Username, string Password);

public record CreateUserRequest(string Username, string Password, string Role, string DisplayName, string? Email);

public record CaseFeedbackRequest(string Action, string? Reason, FeedbackRule? CreateRule);

public record AnalyticsAskRequest(string Prompt, string? Database, string? ConversationId = null, string? Mode = null);

public record CreateConversationRequest(string Database);

public record BulkResolveRequest(List<string> Ids, string Resolution);

public record UpdateUserRequest(string? Role, string? DisplayName, bool? IsActive, string? Password);

public record RenameConversationRequest(string Title);

public record SignupRequest(string Email, string DisplayName, string Password, string ConfirmPassword);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Token, string Password, string ConfirmPassword);

public record VerifyEmailRequest(string Email, string Code);

public record ResendVerificationRequest(string Email);

public record TwoFactorLoginRequest(string Challenge, string Code);

public record RequestLoginOtpRequest(string Email);

public record VerifyLoginOtpRequest(string Email, string Code);

public record TwoFactorCodeRequest(string Code);

public record DisableTwoFactorRequest(string Password);

public record TwoFactorChallenge(string UserId, int Attempts);