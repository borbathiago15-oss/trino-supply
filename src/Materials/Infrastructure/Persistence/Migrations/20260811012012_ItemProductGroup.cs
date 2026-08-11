using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Materials.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ItemProductGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "product_group",
                schema: "materials",
                table: "item",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "Sem grupo");

            migrationBuilder.CreateIndex(
                name: "IX_item_company_id_product_group",
                schema: "materials",
                table: "item",
                columns: new[] { "company_id", "product_group" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_item_company_id_product_group",
                schema: "materials",
                table: "item");

            migrationBuilder.DropColumn(
                name: "product_group",
                schema: "materials",
                table: "item");
        }
    }
}
