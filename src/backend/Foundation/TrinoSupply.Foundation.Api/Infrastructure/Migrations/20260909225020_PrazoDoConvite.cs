using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PrazoDoConvite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "deadline_extensions",
                schema: "procurement",
                table: "quotation_supplier",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "response_deadline",
                schema: "procurement",
                table: "quotation_supplier",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "waived_at",
                schema: "procurement",
                table: "quotation_supplier",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "waived_by_label",
                schema: "procurement",
                table: "quotation_supplier",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "waived_reason",
                schema: "procurement",
                table: "quotation_supplier",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deadline_extensions",
                schema: "procurement",
                table: "quotation_supplier");

            migrationBuilder.DropColumn(
                name: "response_deadline",
                schema: "procurement",
                table: "quotation_supplier");

            migrationBuilder.DropColumn(
                name: "waived_at",
                schema: "procurement",
                table: "quotation_supplier");

            migrationBuilder.DropColumn(
                name: "waived_by_label",
                schema: "procurement",
                table: "quotation_supplier");

            migrationBuilder.DropColumn(
                name: "waived_reason",
                schema: "procurement",
                table: "quotation_supplier");
        }
    }
}
