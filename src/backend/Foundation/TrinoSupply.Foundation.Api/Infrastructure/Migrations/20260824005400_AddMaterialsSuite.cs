using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialsSuite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "mov_number_seq",
                schema: "materials");

            migrationBuilder.CreateSequence(
                name: "mr_number_seq",
                schema: "materials");

            migrationBuilder.CreateTable(
                name: "material_requisition",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    cost_center = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    requester_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    fulfilled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    fulfilled_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    fulfilled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_requisition", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_balance",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    reserved_qty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    last_movement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_movement_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_balance", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_movement",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    origin = table.Column<short>(type: "smallint", nullable: false),
                    origin_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    item_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    balance_before = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    material_requisition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    performed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    performed_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    performed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movement", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "storage_location",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storage_location", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "material_requisition_item",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    stock_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_requisition_item", x => x.id);
                    table.ForeignKey(
                        name: "FK_material_requisition_item_material_requisition_requisition_~",
                        column: x => x.requisition_id,
                        principalSchema: "materials",
                        principalTable: "material_requisition",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_material_requisition_number",
                schema: "materials",
                table: "material_requisition",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_requisition_requester_id_created_at",
                schema: "materials",
                table: "material_requisition",
                columns: new[] { "requester_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_material_requisition_status_created_at",
                schema: "materials",
                table: "material_requisition",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_material_requisition_item_requisition_id",
                schema: "materials",
                table: "material_requisition_item",
                column: "requisition_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_balance_catalog_item_id_location_id",
                schema: "materials",
                table: "stock_balance",
                columns: new[] { "catalog_item_id", "location_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_movement_catalog_item_id_performed_at",
                schema: "materials",
                table: "stock_movement",
                columns: new[] { "catalog_item_id", "performed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movement_location_id_performed_at",
                schema: "materials",
                table: "stock_movement",
                columns: new[] { "location_id", "performed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movement_number",
                schema: "materials",
                table: "stock_movement",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storage_location_code",
                schema: "materials",
                table: "storage_location",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "material_requisition_item",
                schema: "materials");

            migrationBuilder.DropTable(
                name: "stock_balance",
                schema: "materials");

            migrationBuilder.DropTable(
                name: "stock_movement",
                schema: "materials");

            migrationBuilder.DropTable(
                name: "storage_location",
                schema: "materials");

            migrationBuilder.DropTable(
                name: "material_requisition",
                schema: "materials");

            migrationBuilder.DropSequence(
                name: "mov_number_seq",
                schema: "materials");

            migrationBuilder.DropSequence(
                name: "mr_number_seq",
                schema: "materials");
        }
    }
}
