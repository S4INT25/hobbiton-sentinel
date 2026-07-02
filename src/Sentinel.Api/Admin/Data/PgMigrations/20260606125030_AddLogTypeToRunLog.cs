#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Sentinel.Admin.Data.PgMigrations
{
    /// <inheritdoc />
    public partial class AddLogTypeToRunLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "log_type",
                table: "run_logs",
                type: "text",
                nullable: false,
                defaultValue: "tool_call");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "log_type",
                table: "run_logs");
        }
    }
}