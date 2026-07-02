#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Sentinel.Admin.Data.PgMigrations
{
    /// <inheritdoc />
    public partial class WorkflowTimeZoneSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "time_zone_id",
                table: "workflows",
                type: "text",
                nullable: false,
                defaultValue: "Africa/Lusaka");

            migrationBuilder.Sql(
                "UPDATE workflows SET time_zone_id = 'Africa/Lusaka' WHERE time_zone_id IS NULL OR btrim(time_zone_id) = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "time_zone_id",
                table: "workflows");
        }
    }
}