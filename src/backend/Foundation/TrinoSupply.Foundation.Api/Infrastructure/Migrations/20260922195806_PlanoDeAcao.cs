using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlanoDeAcao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "action_item",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    area = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    responsible_id = table.Column<Guid>(type: "uuid", nullable: false),
                    responsible_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expected_gain = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    realized_gain = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    expected_result = table.Column<string>(type: "text", nullable: true),
                    kpi = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cost_center = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    progress = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status_reason = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_item", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_action_item_number",
                schema: "foundation",
                table: "action_item",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_action_item_responsible_id",
                schema: "foundation",
                table: "action_item",
                column: "responsible_id");

            migrationBuilder.CreateIndex(
                name: "IX_action_item_status",
                schema: "foundation",
                table: "action_item",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "action_item",
                schema: "foundation");
        }
    }
}
