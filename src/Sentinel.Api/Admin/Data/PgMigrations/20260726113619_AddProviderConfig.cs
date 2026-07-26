using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sentinel.Admin.Data.PgMigrations
{
    /// <inheritdoc />
    public partial class AddProviderConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create providers table first
            migrationBuilder.CreateTable(
                name: "providers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    api_key = table.Column<string>(type: "text", nullable: false),
                    endpoint = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_providers", x => x.id);
                });

            // 2. Seed the OpenRouter provider so the FK has a valid target
            migrationBuilder.Sql(
                "INSERT INTO providers (display_name, slug, api_key, endpoint, enabled, sort_order, created_at, updated_at) " +
                "VALUES ('OpenRouter', 'openrouter', '', 'https://openrouter.ai/api/v1', true, 0, now(), now());");

            // 3. Add provider_id on llm_models defaulting to the seeded provider (id = 1)
            migrationBuilder.AddColumn<int>(
                name: "provider_id",
                table: "llm_models",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_llm_models_provider_id",
                table: "llm_models",
                column: "provider_id");

            migrationBuilder.CreateIndex(
                name: "IX_providers_enabled",
                table: "providers",
                column: "enabled");

            migrationBuilder.CreateIndex(
                name: "IX_providers_slug",
                table: "providers",
                column: "slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_llm_models_providers_provider_id",
                table: "llm_models",
                column: "provider_id",
                principalTable: "providers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_llm_models_providers_provider_id",
                table: "llm_models");

            migrationBuilder.DropTable(
                name: "providers");

            migrationBuilder.DropIndex(
                name: "IX_llm_models_provider_id",
                table: "llm_models");

            migrationBuilder.DropColumn(
                name: "provider_id",
                table: "llm_models");
        }
    }
}
