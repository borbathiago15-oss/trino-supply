using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductFamilyRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_family",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_family", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_family_name",
                schema: "materials",
                table: "product_family",
                column: "name",
                unique: true);

            // Backfill: cada família já usada nos produtos vira um registro do novo cadastro,
            // para o catálogo existente continuar válido e a lista suspensa já nascer preenchida.
            migrationBuilder.Sql(@"
                INSERT INTO materials.product_family (id, name, active, created_at, updated_at, created_by)
                SELECT gen_random_uuid(), UPPER(TRIM(family)), true, NOW(), NOW(),
                       '00000000-0000-0000-0000-000000000000'
                FROM (SELECT DISTINCT family FROM materials.catalog_item WHERE COALESCE(TRIM(family), '') <> '') f
                ON CONFLICT DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_family",
                schema: "materials");
        }
    }
}
