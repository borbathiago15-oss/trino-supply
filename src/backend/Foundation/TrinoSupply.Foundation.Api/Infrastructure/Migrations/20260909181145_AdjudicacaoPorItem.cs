using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdjudicacaoPorItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_quotation_award_quotation_id_family",
                schema: "procurement",
                table: "quotation_award");

            migrationBuilder.AddColumn<Guid>(
                name: "quotation_item_id",
                schema: "procurement",
                table: "quotation_award",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_quotation_award_quotation_id_family_quotation_item_id",
                schema: "procurement",
                table: "quotation_award",
                columns: new[] { "quotation_id", "family", "quotation_item_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_quotation_award_quotation_id_family_quotation_item_id",
                schema: "procurement",
                table: "quotation_award");

            migrationBuilder.DropColumn(
                name: "quotation_item_id",
                schema: "procurement",
                table: "quotation_award");

            migrationBuilder.CreateIndex(
                name: "IX_quotation_award_quotation_id_family",
                schema: "procurement",
                table: "quotation_award",
                columns: new[] { "quotation_id", "family" },
                unique: true);
        }
    }
}
