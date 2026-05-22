using Hangfire;
using Hangfire.Dashboard.BasicAuthorization;
using Hangfire.Redis.StackExchange;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using OpenAI;
using StackExchange.Redis;
using System.ClientModel;
using FraudDetector.Agent;
using FraudDetector.Infrastructure;
using FraudDetector.Jobs;
using FraudDetector.Memory;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

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
builder.Services.AddScoped<FraudDetectorJob>();
builder.Services.AddHostedService<FraudSchedulerService>();

var app = builder.Build();

var dashOptions = new DashboardOptions { DashboardTitle = "Fraud Detector" };

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