using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinalidadeDaSc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "budget_converted_at",
                schema: "procurement",
                table: "quotation",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "budget_converted_by_label",
                schema: "procurement",
                table: "quotation",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_budget",
                schema: "procurement",
                table: "quotation",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "purpose",
                schema: "procurement",
                table: "purchase_requisition",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "COMPRA");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "budget_converted_at",
                schema: "procurement",
                table: "quotation");

            migrationBuilder.DropColumn(
                name: "budget_converted_by_label",
                schema: "procurement",
                table: "quotation");

            migrationBuilder.DropColumn(
                name: "is_budget",
                schema: "procurement",
                table: "quotation");

            migrationBuilder.DropColumn(
                name: "purpose",
                schema: "procurement",
                table: "purchase_requisition");
        }
    }
}
