using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CustoNaSolicitacaoDeMaterial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "unit_price",
                schema: "materials",
                table: "material_requisition_item",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "unit_price",
                schema: "materials",
                table: "material_requisition_item");
        }
    }
}
