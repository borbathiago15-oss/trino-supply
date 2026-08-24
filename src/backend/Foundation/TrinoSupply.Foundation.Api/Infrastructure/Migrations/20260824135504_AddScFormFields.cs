using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScFormFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "company",
                schema: "procurement",
                table: "purchase_requisition",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_location",
                schema: "procurement",
                table: "purchase_requisition",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "internal_notes",
                schema: "procurement",
                table: "purchase_requisition",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "need_type",
                schema: "procurement",
                table: "purchase_requisition",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "company",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "delivery_location",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "internal_notes",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.DropColumn(
                name: "need_type",
                schema: "procurement",
                table: "purchase_requisition");
        }
    }
}
