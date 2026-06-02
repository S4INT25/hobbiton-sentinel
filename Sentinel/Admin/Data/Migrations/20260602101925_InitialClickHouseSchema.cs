using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sentinel.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialClickHouseSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS sentinel.audit_logs (
                    id UUID,
                    user_id String,
                    username String,
                    action String,
                    resource_type String,
                    resource_id String,
                    details String,
                    ip_address String,
                    timestamp DateTime
                ) ENGINE = MergeTree
                ORDER BY id
                SETTINGS index_granularity = 8192
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS sentinel.evidence_sources (
                    id Int32,
                    name String,
                    evidence_database String,
                    lipila_merchant_ids String DEFAULT '',
                    lipila_partner_id Int32 DEFAULT 0,
                    join_mappings String DEFAULT '[]',
                    table_descriptions String DEFAULT '',
                    evidence_checks String DEFAULT '[]',
                    notes String DEFAULT '',
                    enabled UInt8 DEFAULT 1,
                    created_at DateTime DEFAULT now(),
                    updated_at DateTime DEFAULT now(),
                    created_by String DEFAULT 'system',
                    workflow_id String DEFAULT ''
                ) ENGINE = ReplacingMergeTree(updated_at)
                ORDER BY id
                SETTINGS index_granularity = 8192
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS sentinel.fraud_patterns (
                    id Int32,
                    name String,
                    description String,
                    category String DEFAULT 'TransactionAnomaly',
                    enabled UInt8 DEFAULT 1,
                    created_at DateTime DEFAULT now(),
                    updated_at DateTime DEFAULT now(),
                    created_by String DEFAULT 'system',
                    workflow_id String DEFAULT ''
                ) ENGINE = ReplacingMergeTree(updated_at)
                ORDER BY id
                SETTINGS index_granularity = 8192
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS sentinel.run_logs (
                    run_id String,
                    iteration UInt16,
                    tool_name String,
                    args String,
                    result String,
                    started_at DateTime,
                    duration_ms UInt32
                ) ENGINE = MergeTree
                ORDER BY tuple()
                SETTINGS index_granularity = 8192
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS sentinel.run_summaries (
                    run_id String,
                    started_at DateTime,
                    finished_at DateTime,
                    iterations UInt16,
                    input_tokens UInt32,
                    output_tokens UInt32,
                    cases_created UInt16,
                    cases_resolved UInt16,
                    alerts_sent UInt16,
                    status String,
                    triggered_by String
                ) ENGINE = MergeTree
                ORDER BY tuple()
                SETTINGS index_granularity = 8192
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS sentinel.workflows (
                    id String,
                    name String,
                    description String DEFAULT '',
                    action_type String DEFAULT 'email_report',
                    cron_expression String DEFAULT '0 * * * *',
                    enabled UInt8 DEFAULT 1,
                    target_database String DEFAULT '',
                    sql_query String DEFAULT '',
                    email_subject String DEFAULT '',
                    email_recipients String DEFAULT '',
                    custom_prompt String DEFAULT '',
                    system_prompt String DEFAULT '',
                    is_deleted UInt8 DEFAULT 0,
                    created_at DateTime DEFAULT now(),
                    updated_at DateTime DEFAULT now(),
                    created_by String DEFAULT 'system'
                ) ENGINE = ReplacingMergeTree(updated_at)
                ORDER BY id
                SETTINGS index_granularity = 8192
                """);

            migrationBuilder.Sql("""
                ALTER TABLE sentinel.fraud_patterns
                ADD COLUMN IF NOT EXISTS workflow_id String DEFAULT ''
                """);

            migrationBuilder.Sql("""
                ALTER TABLE sentinel.evidence_sources
                ADD COLUMN IF NOT EXISTS workflow_id String DEFAULT ''
                """);

            migrationBuilder.Sql("""
                ALTER TABLE sentinel.workflows
                MODIFY COLUMN action_type String DEFAULT 'email_report'
                """);

            migrationBuilder.Sql("""
                ALTER TABLE sentinel.workflows
                UPDATE action_type = 'email_report'
                WHERE action_type = 'sql_email_report'
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS sentinel.workflows");
            migrationBuilder.Sql("DROP TABLE IF EXISTS sentinel.run_summaries");
            migrationBuilder.Sql("DROP TABLE IF EXISTS sentinel.run_logs");
            migrationBuilder.Sql("DROP TABLE IF EXISTS sentinel.fraud_patterns");
            migrationBuilder.Sql("DROP TABLE IF EXISTS sentinel.evidence_sources");
            migrationBuilder.Sql("DROP TABLE IF EXISTS sentinel.audit_logs");
        }
    }
}
