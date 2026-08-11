using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Materials.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlmoxarifadoConsumption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ca",
                schema: "materials",
                table: "item",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "collaborator",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    registration = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    cost_center_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    company_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    admission_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collaborator", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "consumption",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    cost_center_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    collaborator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    issued_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumption", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "consumption_line",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumption_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumption_line", x => x.id);
                    table.ForeignKey(
                        name: "FK_consumption_line_consumption_consumption_id",
                        column: x => x.consumption_id,
                        principalSchema: "materials",
                        principalTable: "consumption",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_collaborator_company_id_name",
                schema: "materials",
                table: "collaborator",
                columns: new[] { "company_id", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_consumption_company_id_collaborator_id",
                schema: "materials",
                table: "consumption",
                columns: new[] { "company_id", "collaborator_id" });

            migrationBuilder.CreateIndex(
                name: "IX_consumption_line_company_id_consumption_id",
                schema: "materials",
                table: "consumption_line",
                columns: new[] { "company_id", "consumption_id" });

            migrationBuilder.CreateIndex(
                name: "IX_consumption_line_consumption_id",
                schema: "materials",
                table: "consumption_line",
                column: "consumption_id");

            // --- RLS multi-tenant (SEC-004): colaborador + baixa de consumo isolados por tenant. ---
            migrationBuilder.Sql(@"
ALTER TABLE materials.collaborator     ENABLE ROW LEVEL SECURITY;
ALTER TABLE materials.collaborator     FORCE  ROW LEVEL SECURITY;
ALTER TABLE materials.consumption      ENABLE ROW LEVEL SECURITY;
ALTER TABLE materials.consumption      FORCE  ROW LEVEL SECURITY;
ALTER TABLE materials.consumption_line ENABLE ROW LEVEL SECURITY;
ALTER TABLE materials.consumption_line FORCE  ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON materials.collaborator
    USING (company_id = foundation.current_company()) WITH CHECK (company_id = foundation.current_company());
CREATE POLICY tenant_isolation ON materials.consumption
    USING (company_id = foundation.current_company()) WITH CHECK (company_id = foundation.current_company());
CREATE POLICY tenant_isolation ON materials.consumption_line
    USING (company_id = foundation.current_company()) WITH CHECK (company_id = foundation.current_company());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collaborator",
                schema: "materials");

            migrationBuilder.DropTable(
                name: "consumption_line",
                schema: "materials");

            migrationBuilder.DropTable(
                name: "consumption",
                schema: "materials");

            migrationBuilder.DropColumn(
                name: "ca",
                schema: "materials",
                table: "item");
        }
    }
}
