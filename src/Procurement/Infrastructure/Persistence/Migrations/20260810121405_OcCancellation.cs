using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Procurement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OcCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_purchase_order_company_id_requisition_id",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                schema: "procurement",
                table: "purchase_order",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                schema: "procurement",
                table: "purchase_order",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancelled_by_subject",
                schema: "procurement",
                table: "purchase_order",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_company_id_requisition_id",
                schema: "procurement",
                table: "purchase_order",
                columns: new[] { "company_id", "requisition_id" },
                unique: true,
                filter: "status = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_purchase_order_company_id_requisition_id",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "cancel_reason",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "cancelled_by_subject",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_company_id_requisition_id",
                schema: "procurement",
                table: "purchase_order",
                columns: new[] { "company_id", "requisition_id" },
                unique: true);
        }
    }
}
