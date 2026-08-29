using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotationMultiScGrouping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "source_pr_id",
                schema: "procurement",
                table: "quotation_item",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_pr_item_id",
                schema: "procurement",
                table: "quotation_item",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_pr_number",
                schema: "procurement",
                table: "quotation_item",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_pr_number",
                schema: "procurement",
                table: "purchase_order_item",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_quotation_item_source_pr_id",
                schema: "procurement",
                table: "quotation_item",
                column: "source_pr_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_quotation_item_source_pr_id",
                schema: "procurement",
                table: "quotation_item");

            migrationBuilder.DropColumn(
                name: "source_pr_id",
                schema: "procurement",
                table: "quotation_item");

            migrationBuilder.DropColumn(
                name: "source_pr_item_id",
                schema: "procurement",
                table: "quotation_item");

            migrationBuilder.DropColumn(
                name: "source_pr_number",
                schema: "procurement",
                table: "quotation_item");

            migrationBuilder.DropColumn(
                name: "source_pr_number",
                schema: "procurement",
                table: "purchase_order_item");
        }
    }
}
