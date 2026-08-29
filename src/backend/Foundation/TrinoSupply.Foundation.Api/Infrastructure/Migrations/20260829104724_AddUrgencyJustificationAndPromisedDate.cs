using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUrgencyJustificationAndPromisedDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "urgency_impact",
                schema: "procurement",
                table: "purchase_requisition",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "urgency_reason",
                schema: "procurement",
                table: "purchase_requisition",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "promised_date",
                schema: "procurement",
                table: "purchase_order",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "urgency_impact",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "urgency_reason",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "promised_date",
                schema: "procurement",
                table: "purchase_order");
        }
    }
}
