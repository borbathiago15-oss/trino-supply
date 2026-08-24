using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockItemsAndItemSuppliers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "minimum_qty",
                schema: "materials",
                table: "catalog_item",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "stock_controlled",
                schema: "materials",
                table: "catalog_item",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "catalog_item_supplier",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    tax_id = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    contact = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    supplier_item_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    last_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_item_supplier", x => x.id);
                    table.ForeignKey(
                        name: "FK_catalog_item_supplier_catalog_item_catalog_item_id",
                        column: x => x.catalog_item_id,
                        principalSchema: "materials",
                        principalTable: "catalog_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_item_supplier_catalog_item_id",
                schema: "materials",
                table: "catalog_item_supplier",
                column: "catalog_item_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_item_supplier",
                schema: "materials");

            migrationBuilder.DropColumn(
                name: "minimum_qty",
                schema: "materials",
                table: "catalog_item");

            migrationBuilder.DropColumn(
                name: "stock_controlled",
                schema: "materials",
                table: "catalog_item");
        }
    }
}
