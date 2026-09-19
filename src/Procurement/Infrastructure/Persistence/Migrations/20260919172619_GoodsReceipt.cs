using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Procurement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GoodsReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "goods_receipt",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: true),
                    received_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    stock_posted = table.Column<bool>(type: "boolean", nullable: false),
                    stock_posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_goods_receipt", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "goods_receipt_line",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    quantity_ordered = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    quantity_received = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    quantity_damaged = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    occurrence = table.Column<short>(type: "smallint", nullable: false),
                    occurrence_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_goods_receipt_line", x => x.id);
                    table.ForeignKey(
                        name: "FK_goods_receipt_line_goods_receipt_receipt_id",
                        column: x => x.receipt_id,
                        principalSchema: "procurement",
                        principalTable: "goods_receipt",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipt_company_id_purchase_order_id",
                schema: "procurement",
                table: "goods_receipt",
                columns: new[] { "company_id", "purchase_order_id" });

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipt_line_order_line_id",
                schema: "procurement",
                table: "goods_receipt_line",
                column: "order_line_id");

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipt_line_receipt_id",
                schema: "procurement",
                table: "goods_receipt_line",
                column: "receipt_id");

            // --- RLS multi-tenant nas tabelas novas (SEC-004), mesmo padrão fail-closed. ---
            migrationBuilder.Sql(@"
ALTER TABLE procurement.goods_receipt      ENABLE ROW LEVEL SECURITY;
ALTER TABLE procurement.goods_receipt      FORCE  ROW LEVEL SECURITY;
ALTER TABLE procurement.goods_receipt_line ENABLE ROW LEVEL SECURITY;
ALTER TABLE procurement.goods_receipt_line FORCE  ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON procurement.goods_receipt
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON procurement.goods_receipt_line
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "goods_receipt_line",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "goods_receipt",
                schema: "procurement");
        }
    }
}
