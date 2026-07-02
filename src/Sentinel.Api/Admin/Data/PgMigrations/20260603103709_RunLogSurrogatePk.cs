#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Sentinel.Admin.Data.PgMigrations
{
    /// <inheritdoc />
    public partial class RunLogSurrogatePk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_run_logs",
                table: "run_logs");

            migrationBuilder.AddColumn<long>(
                    name: "id",
                    table: "run_logs",
                    type: "bigint",
                    nullable: false,
                    defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "PK_run_logs",
                table: "run_logs",
                column: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_run_logs",
                table: "run_logs");

            migrationBuilder.DropColumn(
                name: "id",
                table: "run_logs");

            migrationBuilder.AddPrimaryKey(
                name: "PK_run_logs",
                table: "run_logs",
                columns: new[] { "run_id", "started_at", "tool_name" });
        }
    }
}