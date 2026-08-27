using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyLeadTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "lead_approval_to_po",
                schema: "materials",
                table: "product_family",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "lead_po_to_delivery",
                schema: "materials",
                table: "product_family",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "lead_quote_to_approval",
                schema: "materials",
                table: "product_family",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "lead_request_to_quote",
                schema: "materials",
                table: "product_family",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lead_approval_to_po",
                schema: "materials",
                table: "product_family");

            migrationBuilder.DropColumn(
                name: "lead_po_to_delivery",
                schema: "materials",
                table: "product_family");

            migrationBuilder.DropColumn(
                name: "lead_quote_to_approval",
                schema: "materials",
                table: "product_family");

            migrationBuilder.DropColumn(
                name: "lead_request_to_quote",
                schema: "materials",
                table: "product_family");
        }
    }
}
