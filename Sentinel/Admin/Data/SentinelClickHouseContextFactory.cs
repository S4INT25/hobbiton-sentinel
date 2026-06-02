using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Sentinel.Admin.Data;

public class SentinelClickHouseContextFactory : IDesignTimeDbContextFactory<SentinelClickHouseContext>
{
    public SentinelClickHouseContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var chHost = new Uri(configuration["ClickHouse:Host"] ?? "http://localhost:8123");
        var chUser = configuration["ClickHouse:User"] ?? "default";
        var chPass = configuration["ClickHouse:Password"] ?? "";
        var connectionString =
            $"Host={chHost.Host};Port={chHost.Port};Database=sentinel;Username={chUser};Password={chPass}";

        var optionsBuilder = new DbContextOptionsBuilder<SentinelClickHouseContext>();
        optionsBuilder.UseClickHouse(connectionString);
        return new SentinelClickHouseContext(optionsBuilder.Options);
    }
}
