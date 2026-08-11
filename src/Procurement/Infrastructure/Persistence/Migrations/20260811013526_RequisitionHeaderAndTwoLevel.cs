using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Procurement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequisitionHeaderAndTwoLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "decided_by_subject",
                schema: "procurement",
                table: "purchase_requisition",
                newName: "rejected_by");

            migrationBuilder.RenameColumn(
                name: "decided_at",
                schema: "procurement",
                table: "purchase_requisition",
                newName: "rejected_at");

            migrationBuilder.AddColumn<string>(
                name: "approver_l1_subject",
                schema: "procurement",
                table: "purchase_requisition",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "approver_l2_subject",
                schema: "procurement",
                table: "purchase_requisition",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "cost_center_id",
                schema: "procurement",
                table: "purchase_requisition",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "justification",
                schema: "procurement",
                table: "purchase_requisition",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "l1_decided_at",
                schema: "procurement",
                table: "purchase_requisition",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "l1_decided_by",
                schema: "procurement",
                table: "purchase_requisition",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "l2_decided_at",
                schema: "procurement",
                table: "purchase_requisition",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "l2_decided_by",
                schema: "procurement",
                table: "purchase_requisition",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "paying_company_id",
                schema: "procurement",
                table: "purchase_requisition",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<short>(
                name: "priority",
                schema: "procurement",
                table: "purchase_requisition",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.CreateTable(
                name: "cost_center",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cost_center", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cost_center_company_id_code",
                schema: "procurement",
                table: "cost_center",
                columns: new[] { "company_id", "code" },
                unique: true);

            // --- RLS multi-tenant (SEC-004): o cadastro de centro de custo é isolado por tenant. ---
            migrationBuilder.Sql(@"
ALTER TABLE procurement.cost_center ENABLE ROW LEVEL SECURITY;
ALTER TABLE procurement.cost_center FORCE  ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON procurement.cost_center
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cost_center",
                schema: "procurement");

            migrationBuilder.DropColumn(
                name: "approver_l1_subject",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "approver_l2_subject",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "cost_center_id",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "justification",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "l1_decided_at",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "l1_decided_by",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "l2_decided_at",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "l2_decided_by",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "paying_company_id",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "priority",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.RenameColumn(
                name: "rejected_by",
                schema: "procurement",
                table: "purchase_requisition",
                newName: "decided_by_subject");

            migrationBuilder.RenameColumn(
                name: "rejected_at",
                schema: "procurement",
                table: "purchase_requisition",
                newName: "decided_at");
        }
    }
}
