using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrigemDoCiclo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "origin_key",
                schema: "foundation",
                table: "improvement_cycle",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin_label",
                schema: "foundation",
                table: "improvement_cycle",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_improvement_cycle_origin_key",
                schema: "foundation",
                table: "improvement_cycle",
                column: "origin_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_improvement_cycle_origin_key",
                schema: "foundation",
                table: "improvement_cycle");

            migrationBuilder.DropColumn(
                name: "origin_key",
                schema: "foundation",
                table: "improvement_cycle");

            migrationBuilder.DropColumn(
                name: "origin_label",
                schema: "foundation",
                table: "improvement_cycle");
        }
    }
}
