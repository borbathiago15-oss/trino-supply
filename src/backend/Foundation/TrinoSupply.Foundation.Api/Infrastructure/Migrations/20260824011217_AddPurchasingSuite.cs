using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchasingSuite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "po_number_seq",
                schema: "procurement");

            migrationBuilder.CreateTable(
                name: "supplier",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    trade_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tax_id = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    source_pr_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_pr_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    total_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    issued_by = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    received_by = table.Column<Guid>(type: "uuid", nullable: true),
                    received_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_order_supplier_supplier_id",
                        column: x => x.supplier_id,
                        principalSchema: "procurement",
                        principalTable: "supplier",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_item",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    catalog_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order_item", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_order_item_purchase_order_order_id",
                        column: x => x.order_id,
                        principalSchema: "procurement",
                        principalTable: "purchase_order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_number",
                schema: "procurement",
                table: "purchase_order",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_source_pr_id",
                schema: "procurement",
                table: "purchase_order",
                column: "source_pr_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_status_created_at",
                schema: "procurement",
                table: "purchase_order",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_supplier_id",
                schema: "procurement",
                table: "purchase_order",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_item_order_id",
                schema: "procurement",
                table: "purchase_order_item",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_active_legal_name",
                schema: "procurement",
                table: "supplier",
                columns: new[] { "active", "legal_name" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_tax_id",
                schema: "procurement",
                table: "supplier",
                column: "tax_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_order_item",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_order",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "supplier",
                schema: "procurement");

            migrationBuilder.DropSequence(
                name: "po_number_seq",
                schema: "procurement");
        }
    }
}
