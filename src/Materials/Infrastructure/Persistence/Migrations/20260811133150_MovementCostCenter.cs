using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Materials.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MovementCostCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cost_center_code",
                schema: "materials",
                table: "stock_movement",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cost_center_code",
                schema: "materials",
                table: "stock_movement");
        }
    }
}
