using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCostCenterLevelValueLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "level1_value_limit",
                schema: "foundation",
                table: "cost_center",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "level2_value_limit",
                schema: "foundation",
                table: "cost_center",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "level1_value_limit",
                schema: "foundation",
                table: "cost_center");

            migrationBuilder.DropColumn(
                name: "level2_value_limit",
                schema: "foundation",
                table: "cost_center");
        }
    }
}
