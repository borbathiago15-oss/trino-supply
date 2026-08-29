using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProposalCostsReferenceSavingContractLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "contract_value_limit",
                schema: "procurement",
                table: "supplier",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "last_paid_unit_price",
                schema: "procurement",
                table: "purchase_order_item",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "reference_saving",
                schema: "procurement",
                table: "purchase_order_item",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "other_costs",
                schema: "procurement",
                table: "proposal",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_value",
                schema: "procurement",
                table: "proposal",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "contract_value_limit",
                schema: "procurement",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "last_paid_unit_price",
                schema: "procurement",
                table: "purchase_order_item");

            migrationBuilder.DropColumn(
                name: "reference_saving",
                schema: "procurement",
                table: "purchase_order_item");

            migrationBuilder.DropColumn(
                name: "other_costs",
                schema: "procurement",
                table: "proposal");

            migrationBuilder.DropColumn(
                name: "tax_value",
                schema: "procurement",
                table: "proposal");
        }
    }
}
