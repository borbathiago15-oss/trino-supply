using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Procurement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Quotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quotation",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<long>(type: "bigint", nullable: false),
                    requisition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closes_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    cancelled_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quotation_bid",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quotation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    delivery_days = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotation_bid", x => x.id);
                    table.ForeignKey(
                        name: "FK_quotation_bid_quotation_quotation_id",
                        column: x => x.quotation_id,
                        principalSchema: "procurement",
                        principalTable: "quotation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quotation_line",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quotation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisition_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    awarded_supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    awarded_unit_price = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    award_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    awarded_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    awarded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotation_line", x => x.id);
                    table.ForeignKey(
                        name: "FK_quotation_line_quotation_quotation_id",
                        column: x => x.quotation_id,
                        principalSchema: "procurement",
                        principalTable: "quotation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quotation_participant",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quotation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_late = table.Column<bool>(type: "boolean", nullable: false),
                    payment_terms = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    freight_terms = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotation_participant", x => x.id);
                    table.ForeignKey(
                        name: "FK_quotation_participant_quotation_quotation_id",
                        column: x => x.quotation_id,
                        principalSchema: "procurement",
                        principalTable: "quotation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_quotation_company_id_number",
                schema: "procurement",
                table: "quotation",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quotation_company_id_status",
                schema: "procurement",
                table: "quotation",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_quotation_bid_line_id",
                schema: "procurement",
                table: "quotation_bid",
                column: "line_id");

            migrationBuilder.CreateIndex(
                name: "IX_quotation_bid_participant_id_line_id",
                schema: "procurement",
                table: "quotation_bid",
                columns: new[] { "participant_id", "line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quotation_bid_quotation_id",
                schema: "procurement",
                table: "quotation_bid",
                column: "quotation_id");

            migrationBuilder.CreateIndex(
                name: "IX_quotation_line_quotation_id",
                schema: "procurement",
                table: "quotation_line",
                column: "quotation_id");

            migrationBuilder.CreateIndex(
                name: "IX_quotation_line_requisition_line_id",
                schema: "procurement",
                table: "quotation_line",
                column: "requisition_line_id");

            migrationBuilder.CreateIndex(
                name: "IX_quotation_participant_quotation_id_supplier_id",
                schema: "procurement",
                table: "quotation_participant",
                columns: new[] { "quotation_id", "supplier_id" },
                unique: true);

            // --- RLS multi-tenant nas tabelas novas (SEC-004), mesmo padrão fail-closed. ---
            migrationBuilder.Sql(@"
ALTER TABLE procurement.quotation             ENABLE ROW LEVEL SECURITY;
ALTER TABLE procurement.quotation             FORCE  ROW LEVEL SECURITY;
ALTER TABLE procurement.quotation_line        ENABLE ROW LEVEL SECURITY;
ALTER TABLE procurement.quotation_line        FORCE  ROW LEVEL SECURITY;
ALTER TABLE procurement.quotation_participant ENABLE ROW LEVEL SECURITY;
ALTER TABLE procurement.quotation_participant FORCE  ROW LEVEL SECURITY;
ALTER TABLE procurement.quotation_bid         ENABLE ROW LEVEL SECURITY;
ALTER TABLE procurement.quotation_bid         FORCE  ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON procurement.quotation
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON procurement.quotation_line
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON procurement.quotation_participant
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON procurement.quotation_bid
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quotation_bid",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "quotation_line",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "quotation_participant",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "quotation",
                schema: "procurement");
        }
    }
}
