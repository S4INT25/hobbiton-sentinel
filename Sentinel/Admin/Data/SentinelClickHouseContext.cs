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
    public DbSet<EvidenceSource> EvidenceSources => Set<EvidenceSource>();
    public DbSet<WorkflowDefinition> Workflows => Set<WorkflowDefinition>();

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
            e.Property(p => p.WorkflowId).HasColumnName("workflow_id");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            e.Property(p => p.CreatedBy).HasColumnName("created_by");
        });

        modelBuilder.Entity<EvidenceSource>(e =>
        {
            e.HasKey(s => s.Id);
            e.ToTable("evidence_sources");
            e.Property(s => s.Id).HasColumnName("id");
            e.Property(s => s.Name).HasColumnName("name");
            e.Property(s => s.EvidenceDatabase).HasColumnName("evidence_database");
            e.Property(s => s.LipilaMerchantIds).HasColumnName("lipila_merchant_ids");
            e.Property(s => s.LipilaPartnerId).HasColumnName("lipila_partner_id");
            e.Property(s => s.JoinMappings).HasColumnName("join_mappings");
            e.Property(s => s.TableDescriptions).HasColumnName("table_descriptions");
            e.Property(s => s.EvidenceChecks).HasColumnName("evidence_checks");
            e.Property(s => s.Notes).HasColumnName("notes");
            e.Property(s => s.WorkflowId).HasColumnName("workflow_id");
            e.Property(s => s.Enabled).HasColumnName("enabled");
            e.Property(s => s.CreatedAt).HasColumnName("created_at");
            e.Property(s => s.UpdatedAt).HasColumnName("updated_at");
            e.Property(s => s.CreatedBy).HasColumnName("created_by");
        });

        modelBuilder.Entity<WorkflowDefinition>(e =>
        {
            e.HasKey(w => w.Id);
            e.ToTable("workflows");
            e.Property(w => w.Id).HasColumnName("id");
            e.Property(w => w.Name).HasColumnName("name");
            e.Property(w => w.Description).HasColumnName("description");
            e.Property(w => w.ActionType).HasColumnName("action_type");
            e.Property(w => w.CronExpression).HasColumnName("cron_expression");
            e.Property(w => w.Enabled).HasColumnName("enabled");
            e.Property(w => w.TargetDatabase).HasColumnName("target_database");
            e.Property(w => w.SqlQuery).HasColumnName("sql_query");
            e.Property(w => w.EmailSubject).HasColumnName("email_subject");
            e.Property(w => w.EmailRecipients).HasColumnName("email_recipients");
            e.Property(w => w.CustomPrompt).HasColumnName("custom_prompt");
            e.Property(w => w.SystemPrompt).HasColumnName("system_prompt");
            e.Property(w => w.IsDeleted).HasColumnName("is_deleted");
            e.Property(w => w.CreatedAt).HasColumnName("created_at");
            e.Property(w => w.UpdatedAt).HasColumnName("updated_at");
            e.Property(w => w.CreatedBy).HasColumnName("created_by");
        });
    }
}
