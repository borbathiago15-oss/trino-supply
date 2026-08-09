using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Procurement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplierAndOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_order",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_by_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supplier",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tax_id = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "order_line",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_line", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_line_purchase_order_order_id",
                        column: x => x.order_id,
                        principalSchema: "procurement",
                        principalTable: "purchase_order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_line_order_id",
                schema: "procurement",
                table: "order_line",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_company_id_requisition_id",
                schema: "procurement",
                table: "purchase_order",
                columns: new[] { "company_id", "requisition_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_company_id_code",
                schema: "procurement",
                table: "supplier",
                columns: new[] { "company_id", "code" },
                unique: true);

            // --- RLS multi-tenant (SEC-004). ---
            migrationBuilder.Sql(@"
ALTER TABLE procurement.supplier       ENABLE ROW LEVEL SECURITY;
ALTER TABLE procurement.supplier       FORCE  ROW LEVEL SECURITY;
ALTER TABLE procurement.purchase_order ENABLE ROW LEVEL SECURITY;
ALTER TABLE procurement.purchase_order FORCE  ROW LEVEL SECURITY;
ALTER TABLE procurement.order_line     ENABLE ROW LEVEL SECURITY;
ALTER TABLE procurement.order_line     FORCE  ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON procurement.supplier
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON procurement.purchase_order
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON procurement.order_line
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_line",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "supplier",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_order",
                schema: "procurement");
        }
    }
}
