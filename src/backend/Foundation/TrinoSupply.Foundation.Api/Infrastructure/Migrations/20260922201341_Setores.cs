using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Setores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SupplyManagerId",
                schema: "foundation",
                table: "app_user",
                newName: "supply_manager_id");

            migrationBuilder.AddColumn<Guid>(
                name: "sector_id",
                schema: "foundation",
                table: "app_user",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sector",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sector", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sector_code",
                schema: "foundation",
                table: "sector",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sector",
                schema: "foundation");

            migrationBuilder.DropColumn(
                name: "sector_id",
                schema: "foundation",
                table: "app_user");

            migrationBuilder.RenameColumn(
                name: "supply_manager_id",
                schema: "foundation",
                table: "app_user",
                newName: "SupplyManagerId");
        }
    }
}
