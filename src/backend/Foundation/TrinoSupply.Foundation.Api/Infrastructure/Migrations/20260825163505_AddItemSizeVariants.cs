using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemSizeVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "base_code",
                schema: "materials",
                table: "catalog_item",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "size",
                schema: "materials",
                table: "catalog_item",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "base_code",
                schema: "materials",
                table: "catalog_item");

            migrationBuilder.DropColumn(
                name: "size",
                schema: "materials",
                table: "catalog_item");
        }
    }
}
