using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHierarchyLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "foundation",
                table: "cost_center",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cost_centers",
                schema: "foundation",
                table: "app_user",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "director_id",
                schema: "foundation",
                table: "app_user",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "company",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    tax_id = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    state_registration = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    district = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    zip = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_tax_id",
                schema: "foundation",
                table: "company",
                column: "tax_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company",
                schema: "foundation");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "foundation",
                table: "cost_center");

            migrationBuilder.DropColumn(
                name: "cost_centers",
                schema: "foundation",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "director_id",
                schema: "foundation",
                table: "app_user");
        }
    }
}
