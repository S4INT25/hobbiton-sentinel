#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Sentinel.Admin.Data.PgMigrations
{
    /// <inheritdoc />
    public partial class AddDatabaseProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "database_products",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    database_name =
                        table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name =
                        table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_database_products", x => x.id); });

            migrationBuilder.CreateIndex(
                name: "IX_database_products_database_name",
                table: "database_products",
                column: "database_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_database_products_enabled",
                table: "database_products",
                column: "enabled");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "database_products");
        }
    }
}