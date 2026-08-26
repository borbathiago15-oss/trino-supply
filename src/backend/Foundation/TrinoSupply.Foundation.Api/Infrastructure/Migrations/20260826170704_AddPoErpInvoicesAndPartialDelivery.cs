using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPoErpInvoicesAndPartialDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "received_quantity",
                schema: "procurement",
                table: "purchase_order_item",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delivery_completed_at",
                schema: "procurement",
                table: "purchase_order",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "erp_document_id",
                schema: "procurement",
                table: "purchase_order",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "erp_file_name",
                schema: "procurement",
                table: "purchase_order",
                type: "character varying(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "erp_issued_on",
                schema: "procurement",
                table: "purchase_order",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "erp_number",
                schema: "procurement",
                table: "purchase_order",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "purchase_order_invoice",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order_invoice", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_order_invoice_purchase_order_order_id",
                        column: x => x.order_id,
                        principalSchema: "procurement",
                        principalTable: "purchase_order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_invoice_order_id",
                schema: "procurement",
                table: "purchase_order_invoice",
                column: "order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_order_invoice",
                schema: "procurement");

            migrationBuilder.DropColumn(
                name: "received_quantity",
                schema: "procurement",
                table: "purchase_order_item");

            migrationBuilder.DropColumn(
                name: "delivery_completed_at",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "erp_document_id",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "erp_file_name",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "erp_issued_on",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "erp_number",
                schema: "procurement",
                table: "purchase_order");
        }
    }
}
