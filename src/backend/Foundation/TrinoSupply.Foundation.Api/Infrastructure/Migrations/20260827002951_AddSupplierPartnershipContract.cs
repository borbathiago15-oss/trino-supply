using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierPartnershipContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "contract_notes",
                schema: "procurement",
                table: "supplier",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contract_number",
                schema: "procurement",
                table: "supplier",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "contract_valid_from",
                schema: "procurement",
                table: "supplier",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "contract_valid_until",
                schema: "procurement",
                table: "supplier",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "supplier_contract_item",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    catalog_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    unit_of_measure = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    payment_terms = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    payment_days = table.Column<int>(type: "integer", nullable: true),
                    delivery_days = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_contract_item", x => x.id);
                    table.ForeignKey(
                        name: "FK_supplier_contract_item_supplier_supplier_id",
                        column: x => x.supplier_id,
                        principalSchema: "procurement",
                        principalTable: "supplier",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_contract_item_supplier_id_catalog_item_id",
                schema: "procurement",
                table: "supplier_contract_item",
                columns: new[] { "supplier_id", "catalog_item_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_contract_item",
                schema: "procurement");

            migrationBuilder.DropColumn(
                name: "contract_notes",
                schema: "procurement",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "contract_number",
                schema: "procurement",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "contract_valid_from",
                schema: "procurement",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "contract_valid_until",
                schema: "procurement",
                table: "supplier");
        }
    }
}
