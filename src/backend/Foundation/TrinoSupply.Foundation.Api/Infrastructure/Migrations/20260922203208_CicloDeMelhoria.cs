using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CicloDeMelhoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cycle_id",
                schema: "foundation",
                table: "action_item",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "root_cause_ref",
                schema: "foundation",
                table: "action_item",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "improvement_cycle",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    region = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    sector_id = table.Column<Guid>(type: "uuid", nullable: true),
                    areas = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    problem = table.Column<string>(type: "text", nullable: true),
                    current_situation = table.Column<string>(type: "text", nullable: true),
                    tool_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    tool_data = table.Column<string>(type: "text", nullable: true),
                    cause_analysis = table.Column<string>(type: "text", nullable: true),
                    root_cause = table.Column<string>(type: "text", nullable: true),
                    goal_description = table.Column<string>(type: "text", nullable: true),
                    indicator = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    baseline = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    goal_value = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    goal_deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    checked_on = table.Column<DateOnly>(type: "date", nullable: true),
                    result_value = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    check_analysis = table.Column<string>(type: "text", nullable: true),
                    goal_met = table.Column<bool>(type: "boolean", nullable: true),
                    standardization = table.Column<string>(type: "text", nullable: true),
                    lessons = table.Column<string>(type: "text", nullable: true),
                    new_cycle = table.Column<bool>(type: "boolean", nullable: false),
                    phase = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    closed_reason = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_improvement_cycle", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "improvement_cycle_cost_center",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_center = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_improvement_cycle_cost_center", x => x.id);
                    table.ForeignKey(
                        name: "FK_improvement_cycle_cost_center_improvement_cycle_cycle_id",
                        column: x => x.cycle_id,
                        principalSchema: "foundation",
                        principalTable: "improvement_cycle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "improvement_cycle_watcher",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_improvement_cycle_watcher", x => x.id);
                    table.ForeignKey(
                        name: "FK_improvement_cycle_watcher_improvement_cycle_cycle_id",
                        column: x => x.cycle_id,
                        principalSchema: "foundation",
                        principalTable: "improvement_cycle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_action_item_cycle_id",
                schema: "foundation",
                table: "action_item",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "IX_improvement_cycle_code",
                schema: "foundation",
                table: "improvement_cycle",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_improvement_cycle_phase",
                schema: "foundation",
                table: "improvement_cycle",
                column: "phase");

            migrationBuilder.CreateIndex(
                name: "IX_improvement_cycle_sector_id",
                schema: "foundation",
                table: "improvement_cycle",
                column: "sector_id");

            migrationBuilder.CreateIndex(
                name: "IX_improvement_cycle_cost_center_cost_center",
                schema: "foundation",
                table: "improvement_cycle_cost_center",
                column: "cost_center");

            migrationBuilder.CreateIndex(
                name: "IX_improvement_cycle_cost_center_cycle_id_cost_center",
                schema: "foundation",
                table: "improvement_cycle_cost_center",
                columns: new[] { "cycle_id", "cost_center" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_improvement_cycle_watcher_cycle_id_user_id",
                schema: "foundation",
                table: "improvement_cycle_watcher",
                columns: new[] { "cycle_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_improvement_cycle_watcher_user_id",
                schema: "foundation",
                table: "improvement_cycle_watcher",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "improvement_cycle_cost_center",
                schema: "foundation");

            migrationBuilder.DropTable(
                name: "improvement_cycle_watcher",
                schema: "foundation");

            migrationBuilder.DropTable(
                name: "improvement_cycle",
                schema: "foundation");

            migrationBuilder.DropIndex(
                name: "IX_action_item_cycle_id",
                schema: "foundation",
                table: "action_item");

            migrationBuilder.DropColumn(
                name: "cycle_id",
                schema: "foundation",
                table: "action_item");

            migrationBuilder.DropColumn(
                name: "root_cause_ref",
                schema: "foundation",
                table: "action_item");
        }
    }
}
