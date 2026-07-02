#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Sentinel.Admin.Data.PgMigrations
{
    /// <inheritdoc />
    public partial class AddErrorToRunSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "error",
                table: "run_summaries",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "error",
                table: "run_summaries");
        }
    }
}