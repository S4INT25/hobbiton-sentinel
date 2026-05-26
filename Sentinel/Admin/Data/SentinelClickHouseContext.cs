using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Data;

public class SentinelClickHouseContext(DbContextOptions<SentinelClickHouseContext> options)
    : DbContext(options)
{
    public DbSet<RunLog> RunLogs => Set<RunLog>();
    public DbSet<RunSummary> RunSummaries => Set<RunSummary>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RunLog>(e =>
        {
            e.HasNoKey();
            e.ToTable("run_logs");
        });

        modelBuilder.Entity<RunSummary>(e =>
        {
            e.HasNoKey();
            e.ToTable("run_summaries");
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasKey(a => a.Id);
            e.ToTable("audit_logs");
        });
    }
}
