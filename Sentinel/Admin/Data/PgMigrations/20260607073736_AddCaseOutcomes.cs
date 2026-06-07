#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Sentinel.Admin.Data.PgMigrations
{
    /// <inheritdoc />
    public partial class AddCaseOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "case_outcomes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    case_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    pattern_id = table.Column<int>(type: "integer", nullable: true),
                    outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    original_severity =
                        table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    confidence = table.Column<int>(type: "integer", nullable: false),
                    affected_entities = table.Column<string>(type: "text", nullable: false),
                    database = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    workflow_id = table.Column<string>(type: "text", nullable: true),
                    resolution = table.Column<string>(type: "text", nullable: true),
                    resolved_by = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    occurrence_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_case_outcomes", x => x.id); });

            migrationBuilder.CreateIndex(
                name: "IX_case_outcomes_case_id",
                table: "case_outcomes",
                column: "case_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_case_outcomes_category",
                table: "case_outcomes",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "IX_case_outcomes_outcome",
                table: "case_outcomes",
                column: "outcome");

            migrationBuilder.CreateIndex(
                name: "IX_case_outcomes_resolved_at",
                table: "case_outcomes",
                column: "resolved_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "case_outcomes");
        }
    }
}