using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNegotiationSavingAndPaymentDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "baseline_value",
                schema: "procurement",
                table: "quotation",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "negotiated_at",
                schema: "procurement",
                table: "quotation",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "negotiated_by_label",
                schema: "procurement",
                table: "quotation",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "negotiated_value",
                schema: "procurement",
                table: "quotation",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "negotiation_notes",
                schema: "procurement",
                table: "quotation",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "saving_percent",
                schema: "procurement",
                table: "quotation",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "saving_value",
                schema: "procurement",
                table: "quotation",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payment_days",
                schema: "procurement",
                table: "proposal",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "baseline_value",
                schema: "procurement",
                table: "quotation");

            migrationBuilder.DropColumn(
                name: "negotiated_at",
                schema: "procurement",
                table: "quotation");

            migrationBuilder.DropColumn(
                name: "negotiated_by_label",
                schema: "procurement",
                table: "quotation");

            migrationBuilder.DropColumn(
                name: "negotiated_value",
                schema: "procurement",
                table: "quotation");

            migrationBuilder.DropColumn(
                name: "negotiation_notes",
                schema: "procurement",
                table: "quotation");

            migrationBuilder.DropColumn(
                name: "saving_percent",
                schema: "procurement",
                table: "quotation");

            migrationBuilder.DropColumn(
                name: "saving_value",
                schema: "procurement",
                table: "quotation");

            migrationBuilder.DropColumn(
                name: "payment_days",
                schema: "procurement",
                table: "proposal");
        }
    }
}
