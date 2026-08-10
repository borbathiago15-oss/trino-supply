using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Procurement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplierStatsProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processed_event",
                schema: "procurement",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_event", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "supplier_stats",
                schema: "procurement",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orders_count = table.Column<int>(type: "integer", nullable: false),
                    total_value = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    last_order_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_stats", x => new { x.company_id, x.supplier_id });
                });

            // --- RLS multi-tenant (SEC-004): a projeção também é isolada por tenant. ---
            // (processed_event é infra de deduplicação, sem company_id → sem RLS.)
            migrationBuilder.Sql(@"
ALTER TABLE procurement.supplier_stats ENABLE ROW LEVEL SECURITY;
ALTER TABLE procurement.supplier_stats FORCE  ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON procurement.supplier_stats
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_event",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "supplier_stats",
                schema: "procurement");
        }
    }
}
