using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Materials.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockRequestFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_request",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    company_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    cost_center_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    manager_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decision_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    decision_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_request", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_request_line",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_request_line", x => x.id);
                    table.ForeignKey(
                        name: "FK_stock_request_line_stock_request_request_id",
                        column: x => x.request_id,
                        principalSchema: "materials",
                        principalTable: "stock_request",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_request_company_id_requester_subject",
                schema: "materials",
                table: "stock_request",
                columns: new[] { "company_id", "requester_subject" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_request_company_id_status",
                schema: "materials",
                table: "stock_request",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_request_line_company_id_request_id",
                schema: "materials",
                table: "stock_request_line",
                columns: new[] { "company_id", "request_id" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_request_line_request_id",
                schema: "materials",
                table: "stock_request_line",
                column: "request_id");

            // --- RLS multi-tenant (SEC-004): solicitações de almoxarifado isoladas por tenant. ---
            migrationBuilder.Sql(@"
ALTER TABLE materials.stock_request      ENABLE ROW LEVEL SECURITY;
ALTER TABLE materials.stock_request      FORCE  ROW LEVEL SECURITY;
ALTER TABLE materials.stock_request_line ENABLE ROW LEVEL SECURITY;
ALTER TABLE materials.stock_request_line FORCE  ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON materials.stock_request
    USING (company_id = foundation.current_company()) WITH CHECK (company_id = foundation.current_company());
CREATE POLICY tenant_isolation ON materials.stock_request_line
    USING (company_id = foundation.current_company()) WITH CHECK (company_id = foundation.current_company());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_request_line",
                schema: "materials");

            migrationBuilder.DropTable(
                name: "stock_request",
                schema: "materials");
        }
    }
}
