#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Sentinel.Admin.Data.PgMigrations
{
    /// <inheritdoc />
    public partial class InitialPostgresSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    username = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    resource_type = table.Column<string>(type: "text", nullable: false),
                    resource_id = table.Column<string>(type: "text", nullable: false),
                    details = table.Column<string>(type: "text", nullable: false),
                    ip_address = table.Column<string>(type: "text", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_audit_logs", x => x.id); });

            migrationBuilder.CreateTable(
                name: "evidence_sources",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    evidence_database = table.Column<string>(type: "text", nullable: false),
                    lipila_merchant_ids = table.Column<string>(type: "text", nullable: false),
                    lipila_partner_id = table.Column<int>(type: "integer", nullable: false),
                    join_mappings = table.Column<string>(type: "text", nullable: false),
                    table_descriptions = table.Column<string>(type: "text", nullable: false),
                    evidence_checks = table.Column<string>(type: "text", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: false),
                    workflow_id = table.Column<string>(type: "text", nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_evidence_sources", x => x.id); });

            migrationBuilder.CreateTable(
                name: "fraud_patterns",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    workflow_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_fraud_patterns", x => x.id); });

            migrationBuilder.CreateTable(
                name: "run_logs",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "text", nullable: false),
                    tool_name = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    iteration = table.Column<int>(type: "integer", nullable: false),
                    args = table.Column<string>(type: "text", nullable: false),
                    result = table.Column<string>(type: "text", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_run_logs", x => new { x.run_id, x.started_at, x.tool_name });
                });

            migrationBuilder.CreateTable(
                name: "run_summaries",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    iterations = table.Column<int>(type: "integer", nullable: false),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cases_created = table.Column<int>(type: "integer", nullable: false),
                    cases_resolved = table.Column<int>(type: "integer", nullable: false),
                    alerts_sent = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    triggered_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_run_summaries", x => x.run_id); });

            migrationBuilder.CreateTable(
                name: "workflows",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    action_type = table.Column<string>(type: "text", nullable: false),
                    cron_expression = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    target_database = table.Column<string>(type: "text", nullable: false),
                    email_subject = table.Column<string>(type: "text", nullable: false),
                    email_recipients = table.Column<string>(type: "text", nullable: false),
                    custom_prompt = table.Column<string>(type: "text", nullable: false),
                    system_prompt = table.Column<string>(type: "text", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_workflows", x => x.id); });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_timestamp",
                table: "audit_logs",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_user_id",
                table: "audit_logs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_run_logs_run_id",
                table: "run_logs",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "IX_run_summaries_started_at",
                table: "run_summaries",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "IX_run_summaries_triggered_by",
                table: "run_summaries",
                column: "triggered_by");

            migrationBuilder.CreateIndex(
                name: "IX_workflows_enabled",
                table: "workflows",
                column: "enabled");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "evidence_sources");

            migrationBuilder.DropTable(
                name: "fraud_patterns");

            migrationBuilder.DropTable(
                name: "run_logs");

            migrationBuilder.DropTable(
                name: "run_summaries");

            migrationBuilder.DropTable(
                name: "workflows");
        }
    }
}