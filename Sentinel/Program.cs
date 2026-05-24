using Hangfire;
using Hangfire.Dashboard.BasicAuthorization;
using Hangfire.Redis.StackExchange;
using OpenAI;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;
using System.ClientModel;
using Sentinel.Agent;
using Sentinel.Infrastructure;
using Sentinel.Jobs;
using Sentinel.Memory;

// Bootstrap a minimal logger for startup errors before full config is loaded
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

    // ── Serilog — configured via host context so it has access to full config ──
    builder.Host.UseSerilog((ctx, services, cfg) =>
    {
        var seqUrl = ctx.Configuration["Seq:Url"] ?? "http://localhost:5341";
        var seqKey = ctx.Configuration["Seq:ApiKey"];

        cfg
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Hangfire", LogEventLevel.Information)
            .MinimumLevel.Override("MailKit", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Seq(seqUrl, apiKey: seqKey, restrictedToMinimumLevel: LogEventLevel.Debug);
    });

    var redisConnectionString = builder.Configuration["Redis:ConnectionString"];
    var useInMemory = builder.Environment.IsDevelopment() || string.IsNullOrWhiteSpace(redisConnectionString);

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

    if (useInMemory)
    {
        builder.Services.AddSingleton<ICaseStore, InMemoryCaseStore>();
        builder.Services.AddHangfire(config => config.UseInMemoryStorage());
    }
    else
    {
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisConnectionString!));
        builder.Services.AddSingleton<ICaseStore, CaseStore>();
        builder.Services.AddHangfire((sp, config) =>
            config.UseRedisStorage(sp.GetRequiredService<IConnectionMultiplexer>()));
    }

    builder.Services.AddHangfireServer(options =>
    {
        options.WorkerCount = 1;
        options.Queues = ["fraud", "default"];
    });

    builder.Services.AddScoped<FraudAgent>();
    builder.Services.AddScoped<SentinelJob>();
    builder.Services.AddHostedService<FraudSchedulerService>();

    var app = builder.Build();

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