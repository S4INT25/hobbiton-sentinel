using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sentinel.Admin.Data;

/// Design-time factory used by `dotnet ef migrations` tooling.
public class SentinelDbContextFactory : IDesignTimeDbContextFactory<SentinelDbContext>
{
    public SentinelDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<SentinelDbContext>()
            .UseNpgsql("Host=localhost;Database=sentinel;Username=postgres;Password=postgres")
            .Options;
        return new SentinelDbContext(opts);
    }
}