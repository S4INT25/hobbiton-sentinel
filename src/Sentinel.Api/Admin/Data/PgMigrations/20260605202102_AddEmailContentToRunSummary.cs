#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Sentinel.Admin.Data.PgMigrations
{
    /// <inheritdoc />
    public partial class AddEmailContentToRunSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "email_body",
                table: "run_summaries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email_subject",
                table: "run_summaries",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "email_body",
                table: "run_summaries");

            migrationBuilder.DropColumn(
                name: "email_subject",
                table: "run_summaries");
        }
    }
}