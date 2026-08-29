using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPriorityChangeAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "priority_change_reason",
                schema: "procurement",
                table: "purchase_requisition",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "priority_changed_at",
                schema: "procurement",
                table: "purchase_requisition",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "priority_changed_by_label",
                schema: "procurement",
                table: "purchase_requisition",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "priority_change_reason",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "priority_changed_at",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "priority_changed_by_label",
                schema: "procurement",
                table: "purchase_requisition");
        }
    }
}
