using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Materials.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_balance",
                schema: "materials",
                columns: table => new
                {
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_balance", x => x.item_id);
                });

            migrationBuilder.CreateTable(
                name: "stock_movement",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<short>(type: "smallint", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movement", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_balance_company_id",
                schema: "materials",
                table: "stock_balance",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movement_company_id_item_id_occurred_at",
                schema: "materials",
                table: "stock_movement",
                columns: new[] { "company_id", "item_id", "occurred_at" });

            // --- RLS multi-tenant no estoque (SEC-004). ---
            migrationBuilder.Sql(@"
ALTER TABLE materials.stock_balance  ENABLE ROW LEVEL SECURITY;
ALTER TABLE materials.stock_balance  FORCE  ROW LEVEL SECURITY;
ALTER TABLE materials.stock_movement ENABLE ROW LEVEL SECURITY;
ALTER TABLE materials.stock_movement FORCE  ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON materials.stock_balance
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON materials.stock_movement
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_balance",
                schema: "materials");

            migrationBuilder.DropTable(
                name: "stock_movement",
                schema: "materials");
        }
    }
}
