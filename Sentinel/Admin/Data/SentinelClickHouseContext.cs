using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Data;

public class SentinelClickHouseContext(DbContextOptions<SentinelClickHouseContext> options)
    : DbContext(options)
{
    public DbSet<RunLog> RunLogs => Set<RunLog>();
    public DbSet<RunSummary> RunSummaries => Set<RunSummary>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<FraudPatternEntity> FraudPatterns => Set<FraudPatternEntity>();

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

        modelBuilder.Entity<FraudPatternEntity>(e =>
        {
            e.HasKey(p => p.Id);
            e.ToTable("fraud_patterns");
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Name).HasColumnName("name");
            e.Property(p => p.Description).HasColumnName("description");
            e.Property(p => p.Category).HasColumnName("category");
            e.Property(p => p.Enabled).HasColumnName("enabled");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
        });
    }
}
