using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OcsDoErpPorPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_order_erp_document",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order_erp_document", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_order_erp_document_purchase_order_order_id",
                        column: x => x.order_id,
                        principalSchema: "procurement",
                        principalTable: "purchase_order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_erp_document_item",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    erp_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order_erp_document_item", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_order_erp_document_item_purchase_order_erp_documen~",
                        column: x => x.erp_document_id,
                        principalSchema: "procurement",
                        principalTable: "purchase_order_erp_document",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_erp_document_number",
                schema: "procurement",
                table: "purchase_order_erp_document",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_erp_document_order_id",
                schema: "procurement",
                table: "purchase_order_erp_document",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_erp_document_item_erp_document_id",
                schema: "procurement",
                table: "purchase_order_erp_document_item",
                column: "erp_document_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_erp_document_item_order_item_id",
                schema: "procurement",
                table: "purchase_order_erp_document_item",
                column: "order_item_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_order_erp_document_item",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_order_erp_document",
                schema: "procurement");
        }
    }
}
