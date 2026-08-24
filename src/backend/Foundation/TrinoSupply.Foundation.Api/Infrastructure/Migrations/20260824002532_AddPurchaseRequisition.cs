using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseRequisition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "procurement");

            migrationBuilder.CreateSequence(
                name: "pr_number_seq",
                schema: "procurement");

            migrationBuilder.CreateTable(
                name: "purchase_requisition",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    cycle = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    needed_by = table.Column<DateOnly>(type: "date", nullable: true),
                    justification = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    cost_center = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    requester_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    decision_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    decided_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_requisition", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_requisition_item",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    estimated_unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_requisition_item", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_requisition_item_purchase_requisition_requisition_~",
                        column: x => x.requisition_id,
                        principalSchema: "procurement",
                        principalTable: "purchase_requisition",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_requisition_number",
                schema: "procurement",
                table: "purchase_requisition",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_requisition_requester_id_created_at",
                schema: "procurement",
                table: "purchase_requisition",
                columns: new[] { "requester_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_requisition_status_submitted_at",
                schema: "procurement",
                table: "purchase_requisition",
                columns: new[] { "status", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_requisition_item_requisition_id",
                schema: "procurement",
                table: "purchase_requisition_item",
                column: "requisition_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_requisition_item",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_requisition",
                schema: "procurement");

            migrationBuilder.DropSequence(
                name: "pr_number_seq",
                schema: "procurement");
        }
    }
}
