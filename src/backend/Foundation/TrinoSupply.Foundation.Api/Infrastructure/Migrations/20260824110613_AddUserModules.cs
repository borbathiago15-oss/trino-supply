using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "modules",
                schema: "foundation",
                table: "app_user",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "modules",
                schema: "foundation",
                table: "app_user");
        }
    }
}
