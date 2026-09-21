using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sentinel.Admin.Data.PgMigrations
{
    /// <inheritdoc />
    public partial class AddTripwireAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tripwire_alerts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    rule_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    detail = table.Column<string>(type: "text", nullable: false),
                    run_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    fired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tripwire_alerts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tripwire_alerts_rule_name_entity_key_fired_at",
                table: "tripwire_alerts",
                columns: new[] { "rule_name", "entity_key", "fired_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tripwire_alerts");
        }
    }
}
