using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialApprovalAndFulfillment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "approved_quantity",
                schema: "materials",
                table: "material_requisition_item",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "fulfilled_quantity",
                schema: "materials",
                table: "material_requisition_item",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                schema: "materials",
                table: "material_requisition",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by_id",
                schema: "materials",
                table: "material_requisition",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approved_by_label",
                schema: "materials",
                table: "material_requisition",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision_reason",
                schema: "materials",
                table: "material_requisition",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "purchase_requisition_id",
                schema: "materials",
                table: "material_requisition",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "purchase_requisition_number",
                schema: "materials",
                table: "material_requisition",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approved_quantity",
                schema: "materials",
                table: "material_requisition_item");

            migrationBuilder.DropColumn(
                name: "fulfilled_quantity",
                schema: "materials",
                table: "material_requisition_item");

            migrationBuilder.DropColumn(
                name: "approved_at",
                schema: "materials",
                table: "material_requisition");

            migrationBuilder.DropColumn(
                name: "approved_by_id",
                schema: "materials",
                table: "material_requisition");

            migrationBuilder.DropColumn(
                name: "approved_by_label",
                schema: "materials",
                table: "material_requisition");

            migrationBuilder.DropColumn(
                name: "decision_reason",
                schema: "materials",
                table: "material_requisition");

            migrationBuilder.DropColumn(
                name: "purchase_requisition_id",
                schema: "materials",
                table: "material_requisition");

            migrationBuilder.DropColumn(
                name: "purchase_requisition_number",
                schema: "materials",
                table: "material_requisition");
        }
    }
}
