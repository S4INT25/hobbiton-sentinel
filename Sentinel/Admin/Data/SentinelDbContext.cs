using Microsoft.EntityFrameworkCore;
using Sentinel.Admin.Models;

namespace Sentinel.Admin.Data;

public class SentinelDbContext(DbContextOptions<SentinelDbContext> options) : DbContext(options)
{
    public DbSet<RunLog>             RunLogs         => Set<RunLog>();
    public DbSet<RunSummary>         RunSummaries    => Set<RunSummary>();
    public DbSet<AuditLog>           AuditLogs       => Set<AuditLog>();
    public DbSet<FraudPatternEntity> FraudPatterns   => Set<FraudPatternEntity>();
    public DbSet<EvidenceSource>     EvidenceSources => Set<EvidenceSource>();
    public DbSet<WorkflowDefinition> Workflows       => Set<WorkflowDefinition>();
    public DbSet<AgentMemory>        AgentMemories   => Set<AgentMemory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RunSummary>(e =>
        {
            e.HasKey(r => r.RunId);
            e.ToTable("run_summaries");
            e.Property(r => r.RunId).HasColumnName("run_id");
            e.Property(r => r.StartedAt).HasColumnName("started_at");
            e.Property(r => r.FinishedAt).HasColumnName("finished_at");
            e.Property(r => r.Iterations).HasColumnName("iterations");
            e.Property(r => r.InputTokens).HasColumnName("input_tokens");
            e.Property(r => r.OutputTokens).HasColumnName("output_tokens");
            e.Property(r => r.CasesCreated).HasColumnName("cases_created");
            e.Property(r => r.CasesResolved).HasColumnName("cases_resolved");
            e.Property(r => r.AlertsSent).HasColumnName("alerts_sent");
            e.Property(r => r.Status).HasColumnName("status");
            e.Property(r => r.TriggeredBy).HasColumnName("triggered_by");
            e.Property(r => r.EmailSubject).HasColumnName("email_subject");
            e.Property(r => r.EmailBody).HasColumnName("email_body");
            e.HasIndex(r => r.StartedAt);
            e.HasIndex(r => r.TriggeredBy);
        });

        modelBuilder.Entity<RunLog>(e =>
        {
            e.HasKey(r => r.Id);
            e.ToTable("run_logs");
            e.Property(r => r.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(r => r.RunId).HasColumnName("run_id");
            e.Property(r => r.Iteration).HasColumnName("iteration");
            e.Property(r => r.ToolName).HasColumnName("tool_name");
            e.Property(r => r.Args).HasColumnName("args");
            e.Property(r => r.Result).HasColumnName("result");
            e.Property(r => r.StartedAt).HasColumnName("started_at");
            e.Property(r => r.DurationMs).HasColumnName("duration_ms");
            e.HasIndex(r => r.RunId);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasKey(a => a.Id);
            e.ToTable("audit_logs");
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.UserId).HasColumnName("user_id");
            e.Property(a => a.Username).HasColumnName("username");
            e.Property(a => a.Action).HasColumnName("action");
            e.Property(a => a.ResourceType).HasColumnName("resource_type");
            e.Property(a => a.ResourceId).HasColumnName("resource_id");
            e.Property(a => a.Details).HasColumnName("details");
            e.Property(a => a.IpAddress).HasColumnName("ip_address");
            e.Property(a => a.Timestamp).HasColumnName("timestamp");
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => a.UserId);
        });

        modelBuilder.Entity<FraudPatternEntity>(e =>
        {
            e.HasKey(p => p.Id);
            e.ToTable("fraud_patterns");
            e.Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
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
            e.Property(s => s.Id).HasColumnName("id").ValueGeneratedOnAdd();
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
            e.Property(w => w.TimeZoneId).HasColumnName("time_zone_id");
            e.Property(w => w.Enabled).HasColumnName("enabled");
            e.Property(w => w.TargetDatabase).HasColumnName("target_database");
            e.Property(w => w.EmailSubject).HasColumnName("email_subject");
            e.Property(w => w.EmailRecipients).HasColumnName("email_recipients");
            e.Property(w => w.CustomPrompt).HasColumnName("custom_prompt");
            e.Property(w => w.SystemPrompt).HasColumnName("system_prompt");
            e.Property(w => w.IsDeleted).HasColumnName("is_deleted");
            e.Property(w => w.CreatedAt).HasColumnName("created_at");
            e.Property(w => w.UpdatedAt).HasColumnName("updated_at");
            e.Property(w => w.CreatedBy).HasColumnName("created_by");
            e.HasIndex(w => w.Enabled);
        });

        modelBuilder.Entity<AgentMemory>(e =>
        {
            e.HasKey(m => m.Id);
            e.ToTable("agent_memories");
            e.Property(m => m.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(m => m.Term).HasColumnName("term").HasMaxLength(200);
            e.Property(m => m.Definition).HasColumnName("definition");
            e.Property(m => m.Database).HasColumnName("database").HasMaxLength(100);
            e.Property(m => m.Enabled).HasColumnName("enabled");
            e.Property(m => m.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
            e.Property(m => m.CreatedAt).HasColumnName("created_at");
            e.Property(m => m.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(m => m.Enabled);
            e.HasIndex(m => m.Term);
        });
    }
}
