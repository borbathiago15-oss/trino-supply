using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotationAwardByFamily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "family",
                schema: "procurement",
                table: "quotation_item",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "family",
                schema: "procurement",
                table: "purchase_order_item",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "families",
                schema: "procurement",
                table: "purchase_order",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "quotation_award",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quotation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_version = table.Column<int>(type: "integer", nullable: false),
                    items_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    total_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    criteria = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    justification = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    selected_by = table.Column<Guid>(type: "uuid", nullable: false),
                    selected_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    selected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purchase_order_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotation_award", x => x.id);
                    table.ForeignKey(
                        name: "FK_quotation_award_quotation_quotation_id",
                        column: x => x.quotation_id,
                        principalSchema: "procurement",
                        principalTable: "quotation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_quotation_award_quotation_id_family",
                schema: "procurement",
                table: "quotation_award",
                columns: new[] { "quotation_id", "family" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quotation_award",
                schema: "procurement");

            migrationBuilder.DropColumn(
                name: "family",
                schema: "procurement",
                table: "quotation_item");

            migrationBuilder.DropColumn(
                name: "family",
                schema: "procurement",
                table: "purchase_order_item");

            migrationBuilder.DropColumn(
                name: "families",
                schema: "procurement",
                table: "purchase_order");
        }
    }
}
