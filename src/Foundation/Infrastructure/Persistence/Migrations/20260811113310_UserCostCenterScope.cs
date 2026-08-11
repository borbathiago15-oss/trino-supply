using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserCostCenterScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "cost_center_codes",
                schema: "foundation",
                table: "app_user",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cost_center_codes",
                schema: "foundation",
                table: "app_user");
        }
    }
}
