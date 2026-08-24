using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDemandTriage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "assigned_at",
                schema: "procurement",
                table: "purchase_requisition",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "assigned_by_id",
                schema: "procurement",
                table: "purchase_requisition",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "assigned_by_label",
                schema: "procurement",
                table: "purchase_requisition",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "assigned_to_id",
                schema: "procurement",
                table: "purchase_requisition",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "assigned_to_label",
                schema: "procurement",
                table: "purchase_requisition",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "assigned_at",
                schema: "materials",
                table: "material_requisition",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "assigned_by_id",
                schema: "materials",
                table: "material_requisition",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "assigned_by_label",
                schema: "materials",
                table: "material_requisition",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "assigned_to_id",
                schema: "materials",
                table: "material_requisition",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "assigned_to_label",
                schema: "materials",
                table: "material_requisition",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "assigned_at",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "assigned_by_id",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "assigned_by_label",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "assigned_to_id",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "assigned_to_label",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "assigned_at",
                schema: "materials",
                table: "material_requisition");

            migrationBuilder.DropColumn(
                name: "assigned_by_id",
                schema: "materials",
                table: "material_requisition");

            migrationBuilder.DropColumn(
                name: "assigned_by_label",
                schema: "materials",
                table: "material_requisition");

            migrationBuilder.DropColumn(
                name: "assigned_to_id",
                schema: "materials",
                table: "material_requisition");

            migrationBuilder.DropColumn(
                name: "assigned_to_label",
                schema: "materials",
                table: "material_requisition");
        }
    }
}
