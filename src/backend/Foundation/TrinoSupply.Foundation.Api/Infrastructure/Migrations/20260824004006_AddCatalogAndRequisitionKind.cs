using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogAndRequisitionKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "materials");

            migrationBuilder.AddColumn<string>(
                name: "catalog_code",
                schema: "procurement",
                table: "purchase_requisition_item",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "catalog_item_id",
                schema: "procurement",
                table: "purchase_requisition_item",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "procurement",
                table: "purchase_requisition",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "catalog_item",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    family = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reference_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_item", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_item_code",
                schema: "materials",
                table: "catalog_item",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_catalog_item_family_active",
                schema: "materials",
                table: "catalog_item",
                columns: new[] { "family", "active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_item",
                schema: "materials");

            migrationBuilder.DropColumn(
                name: "catalog_code",
                schema: "procurement",
                table: "purchase_requisition_item");

            migrationBuilder.DropColumn(
                name: "catalog_item_id",
                schema: "procurement",
                table: "purchase_requisition_item");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "procurement",
                table: "purchase_requisition");
        }
    }
}
