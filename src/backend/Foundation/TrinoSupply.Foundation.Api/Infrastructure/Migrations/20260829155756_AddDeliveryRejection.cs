using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryRejection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "rejected_quantity",
                schema: "procurement",
                table: "purchase_order_item",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                schema: "procurement",
                table: "purchase_order_item",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rejected_quantity",
                schema: "procurement",
                table: "purchase_order_item");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                schema: "procurement",
                table: "purchase_order_item");
        }
    }
}
