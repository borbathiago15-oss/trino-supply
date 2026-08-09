using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Materials.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplenishmentPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "replenishment_policy",
                schema: "materials",
                columns: table => new
                {
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_level = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    max_level = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_replenishment_policy", x => x.item_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_replenishment_policy_company_id",
                schema: "materials",
                table: "replenishment_policy",
                column: "company_id");

            // --- RLS multi-tenant (SEC-004). ---
            migrationBuilder.Sql(@"
ALTER TABLE materials.replenishment_policy ENABLE ROW LEVEL SECURITY;
ALTER TABLE materials.replenishment_policy FORCE  ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON materials.replenishment_policy
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "replenishment_policy",
                schema: "materials");
        }
    }
}
