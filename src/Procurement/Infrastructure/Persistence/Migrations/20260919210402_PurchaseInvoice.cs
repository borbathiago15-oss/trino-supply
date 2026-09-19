using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Procurement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_invoice",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_key = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: false),
                    number = table.Column<long>(type: "bigint", nullable: false),
                    series = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    emitter_tax_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    emitter_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    total_value = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    imported_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    matched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    match_summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    released_to_finance = table.Column<bool>(type: "boolean", nullable: false),
                    released_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    release_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_invoice", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_invoice_line",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_number = table.Column<int>(type: "integer", nullable: false),
                    product_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ncm = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    cfop = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    total_value = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_invoice_line", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_invoice_line_purchase_invoice_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "procurement",
                        principalTable: "purchase_invoice",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_invoice_company_id_access_key",
                schema: "procurement",
                table: "purchase_invoice",
                columns: new[] { "company_id", "access_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_invoice_company_id_purchase_order_id",
                schema: "procurement",
                table: "purchase_invoice",
                columns: new[] { "company_id", "purchase_order_id" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_invoice_line_invoice_id",
                schema: "procurement",
                table: "purchase_invoice_line",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_invoice_line_invoice_id_item_number",
                schema: "procurement",
                table: "purchase_invoice_line",
                columns: new[] { "invoice_id", "item_number" },
                unique: true);

            // --- RLS multi-tenant nas tabelas novas (SEC-004), mesmo padrão fail-closed. ---
            migrationBuilder.Sql(@"
ALTER TABLE procurement.purchase_invoice      ENABLE ROW LEVEL SECURITY;
ALTER TABLE procurement.purchase_invoice      FORCE  ROW LEVEL SECURITY;
ALTER TABLE procurement.purchase_invoice_line ENABLE ROW LEVEL SECURITY;
ALTER TABLE procurement.purchase_invoice_line FORCE  ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON procurement.purchase_invoice
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON procurement.purchase_invoice_line
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_invoice_line",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_invoice",
                schema: "procurement");
        }
    }
}
