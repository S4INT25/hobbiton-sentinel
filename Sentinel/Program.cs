using Hangfire;
using Hangfire.Dashboard.BasicAuthorization;
using Hangfire.Redis.StackExchange;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;
using System.ClientModel;
using Sentinel.Admin;
using Sentinel.Admin.Auth;
using Sentinel.Admin.Data;
using Sentinel.Admin.Stores;
using Sentinel.Agent;
using ApexCharts;
using Sentinel.Infrastructure;
using Sentinel.Jobs;
using Sentinel.Memory;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddHttpContextAccessor();

    builder.Configuration
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
        .AddEnvironmentVariables();

    builder.Host.UseSerilog((ctx, _, cfg) =>
    {
        var seqUrl = ctx.Configuration["Seq:Url"] ?? "http://localhost:5341";
        var seqKey = ctx.Configuration["Seq:ApiKey"];

        cfg
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Hangfire", LogEventLevel.Information)
            .MinimumLevel.Override("MailKit", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .MinimumLevel.Override("ZiggyCreatures.Caching.Fusion", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Seq(seqUrl, apiKey: seqKey, restrictedToMinimumLevel: LogEventLevel.Information);
    });

    var redisConnectionString = builder.Configuration["Redis:ConnectionString"];
    var postgresConnectionString = builder.Configuration.GetConnectionString("Sentinel");
    var usePostgresStores = !string.IsNullOrWhiteSpace(postgresConnectionString);
    var useRedisInfrastructure = !string.IsNullOrWhiteSpace(redisConnectionString);

    builder.Services.AddSingleton(_ =>
    {
        var cfg = builder.Configuration.GetSection("DigitalOcean");
        return new OpenAIClient(
            new ApiKeyCredential(cfg["InferenceKey"]!),
            new OpenAIClientOptions { Endpoint = new Uri(cfg["InferenceEndpoint"]!) });
    });

    builder.Services.AddHttpClient<ClickHouseClient>();
    builder.Services.AddHttpClient<IpLookupClient>();
    builder.Services.AddSingleton<EmailClient>();

    // Stores backed by FusionCache — single implementation for dev and prod
    builder.Services.AddSingleton<ICaseStore, CaseStore>();
    builder.Services.AddSingleton<IFeedbackRuleStore, FeedbackRuleStore>();
    builder.Services.AddSingleton<IUserStore, UserStore>();
    builder.Services.AddSingleton<ISystemPromptStore, SystemPromptStore>();
    builder.Services.AddSingleton<IAnalyticsChatStore, AnalyticsChatStore>();
    builder.Services.AddSingleton<IAnalyticsJobStore, AnalyticsJobStore>();
    builder.Services.AddSingleton<IActiveRunTracker, ActiveRunTracker>();

    if (usePostgresStores)
    {
        builder.Services.AddDbContextFactory<SentinelDbContext>(
            options => options.UseNpgsql(postgresConnectionString),
            ServiceLifetime.Scoped);

        builder.Services.AddScoped<IRunLogStore, PostgresRunLogStore>();
        builder.Services.AddScoped<IAuditLogStore, PostgresAuditLogStore>();
        builder.Services.AddScoped<IFraudPatternStore, PostgresFraudPatternStore>();
        builder.Services.AddScoped<IEvidenceSourceStore, PostgresEvidenceSourceStore>();
        builder.Services.AddScoped<IWorkflowStore, PostgresWorkflowStore>();
        builder.Services.AddScoped<IAgentMemoryStore, AgentMemoryStore>();
    }
    else
    {
        builder.Services.AddSingleton<IRunLogStore, InMemoryRunLogStore>();
        builder.Services.AddSingleton<IAuditLogStore, InMemoryAuditLogStore>();
        builder.Services.AddSingleton<IFraudPatternStore, InMemoryFraudPatternStore>();
        builder.Services.AddSingleton<IEvidenceSourceStore, InMemoryEvidenceSourceStore>();
        builder.Services.AddSingleton<IWorkflowStore, InMemoryWorkflowStore>();
    }

    if (useRedisInfrastructure)
    {
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisConnectionString!));

        // L2 distributed cache — Redis as persistent cache storage
        builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConnectionString!);
        builder.Services.AddFusionCache()
            .WithSerializer(new FusionCacheSystemTextJsonSerializer())
            .WithRegisteredDistributedCache()
            .WithStackExchangeRedisBackplane(o => o.Configuration = redisConnectionString!)
            .AsHybridCache();
        builder.Services.AddHangfire((sp, config) =>
            config.UseRedisStorage(sp.GetRequiredService<IConnectionMultiplexer>()));
    }
    else
    {
        builder.Services.AddFusionCache();
        builder.Services.AddHangfire(config => config.UseInMemoryStorage());
    }

    builder.Services.AddSingleton<SchemaLoader>();
    builder.Services.AddSingleton<RunCancellationRegistry>();

    builder.Services.AddHangfireServer(options =>
    {
        options.WorkerCount = 1;
        options.Queues = ["fraud", "default"];
    });

    builder.Services.AddScoped<FraudAgent>();
    builder.Services.AddScoped<SentinelJob>();
    builder.Services.AddScoped<WorkflowExecutionJob>();
    builder.Services.AddScoped<AnalyticsAgent>();
    builder.Services.AddSingleton<WorkflowSchedulerService>();
    builder.Services.AddSingleton<AnalyticsQueryWorker>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<AnalyticsQueryWorker>());
    builder.Services.AddHostedService<FraudSchedulerService>();
    builder.Services.AddApexCharts();

    // ── Authentication & Authorization ──
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/admin/login";
            options.LogoutPath = "/admin/logout";
            options.AccessDeniedPath = "/admin/chat";
            options.Cookie.Name = "sentinel_auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromHours(
                builder.Configuration.GetValue("Admin:SessionHours", 8));
            options.SlidingExpiration = true;
        });

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy(AuthConstants.AdminOnlyPolicy, p => p.RequireRole(AuthConstants.AdminRole));
        options.AddPolicy(AuthConstants.AdminOrDeveloperPolicy, p =>
            p.RequireRole(AuthConstants.AdminRole, AuthConstants.DeveloperRole));
        options.AddPolicy(AuthConstants.Policy, p =>
            p.RequireRole(AuthConstants.AdminRole, AuthConstants.AnalystRole, AuthConstants.DeveloperRole));
    });

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents(options =>
        {
            options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(30);
            options.JSInteropDefaultCallTimeout = TimeSpan.FromSeconds(60);
        });
    builder.Services.AddCascadingAuthenticationState();

    var app = builder.Build();


    using var scope = app.Services.CreateScope();
    if (usePostgresStores)
    {
        var pgFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SentinelDbContext>>();
        await using var pgDb = await pgFactory.CreateDbContextAsync();
        await pgDb.Database.MigrateAsync();
    }

    var patternStore = scope.ServiceProvider.GetRequiredService<IFraudPatternStore>();
    await patternStore.SeedDefaultsAsync();
    var evidenceStore = scope.ServiceProvider.GetRequiredService<IEvidenceSourceStore>();
    await evidenceStore.SeedDefaultsAsync();
    var workflowStore = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
    await workflowStore.SeedDefaultsAsync();
    await SeedAdminUser(app);

    // Warm schema cache for all databases at startup (non-blocking)
    _ = Task.Run(async () =>
    {
        try
        {
            var schemaLoader = app.Services.GetRequiredService<SchemaLoader>();
            await schemaLoader.WarmAllAsync();
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Schema warm-up failed — will load on demand");
        }
    });

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseStaticFiles();
    app.UseAntiforgery();
    app.MapGet("/", () => Results.Redirect("/admin/chat"));
    app.MapRazorComponents<Sentinel.Admin.Components.App>()
        .AddInteractiveServerRenderMode();

    app.MapAdminApi();

    var dashOptions = new DashboardOptions { DashboardTitle = "Sentinel" };

    if (!app.Environment.IsDevelopment())
    {
        var dashUser = app.Configuration["Dashboard:Username"] ?? "admin";
        var dashPass = app.Configuration["Dashboard:Password"] ?? "admin";
        dashOptions.Authorization =
        [
            new BasicAuthAuthorizationFilter(new BasicAuthAuthorizationFilterOptions
            {
                RequireSsl = false,
                SslRedirect = false,
                LoginCaseSensitive = true,
                Users = [new BasicAuthAuthorizationUser { Login = dashUser, PasswordClear = dashPass }]
            })
        ];
    }

    app.UseHangfireDashboard("/hangfire", dashOptions);

    app.Run();
}
catch (Exception ex) when (ex is not OperationCanceledException && ex.GetType().Name != "StopTheHostException")
{
    Log.Fatal(ex, "Sentinel terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

static async Task SeedAdminUser(WebApplication app)
{
    var userStore = app.Services.GetRequiredService<IUserStore>();
    var config = app.Configuration;

    var existingUsers = await userStore.GetAllAsync();
    if (existingUsers.Count > 0) return;

    var username = config["Admin:DefaultUsername"] ?? "admin";
    var password = config["Admin:DefaultPassword"] ?? "sentinel2025!";

    var user = new Sentinel.Admin.Models.AdminUser
    {
        Username = username,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
        Role = AuthConstants.AdminRole,
        DisplayName = "System Admin",
        IsActive = true
    };

    await userStore.SaveAsync(user);
    Log.Information("Seeded default admin user: {Username}", username);
}