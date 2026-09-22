using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlanoDeAcaoComProjeto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_action_item_cycle_id",
                schema: "foundation",
                table: "action_item");

            migrationBuilder.DropColumn(
                name: "cycle_id",
                schema: "foundation",
                table: "action_item");

            migrationBuilder.AddColumn<string>(
                name: "comments",
                schema: "foundation",
                table: "action_item",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "complexity",
                schema: "foundation",
                table: "action_item",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "dependencies",
                schema: "foundation",
                table: "action_item",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "evidence",
                schema: "foundation",
                table: "action_item",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "plan_id",
                schema: "foundation",
                table: "action_item",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "risk_level",
                schema: "foundation",
                table: "action_item",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "seq",
                schema: "foundation",
                table: "action_item",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "support_area",
                schema: "foundation",
                table: "action_item",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "action_plan",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    cost_center = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    areas = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    other_area = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    completion = table.Column<int>(type: "integer", nullable: false),
                    problem = table.Column<string>(type: "text", nullable: true),
                    business_reason = table.Column<string>(type: "text", nullable: true),
                    category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    sponsor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    manager_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    operational_impact = table.Column<string>(type: "text", nullable: true),
                    financial_impact = table.Column<string>(type: "text", nullable: true),
                    kpi_affected = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    target_goal = table.Column<string>(type: "text", nullable: true),
                    criticality = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    complexity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    roi_expected = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    saving_expected = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    saving_realized = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    investment_planned = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    investment_actual = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    cancelled = table.Column<bool>(type: "boolean", nullable: false),
                    cancel_reason = table.Column<string>(type: "text", nullable: true),
                    life = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    closed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    evidence_note = table.Column<string>(type: "text", nullable: true),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    auto_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_plan", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "action_plan_responsible",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_plan_responsible", x => x.id);
                    table.ForeignKey(
                        name: "FK_action_plan_responsible_action_plan_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "foundation",
                        principalTable: "action_plan",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plan_lesson",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    what_worked = table.Column<string>(type: "text", nullable: true),
                    what_failed = table.Column<string>(type: "text", nullable: true),
                    lessons = table.Column<string>(type: "text", nullable: true),
                    best_practice = table.Column<string>(type: "text", nullable: true),
                    next_steps = table.Column<string>(type: "text", nullable: true),
                    recommendation = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_lesson", x => x.id);
                    table.ForeignKey(
                        name: "FK_plan_lesson_action_plan_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "foundation",
                        principalTable: "action_plan",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plan_risk",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    probability = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    impact = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mitigation = table.Column<string>(type: "text", nullable: true),
                    responsible_id = table.Column<Guid>(type: "uuid", nullable: true),
                    responsible_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_risk", x => x.id);
                    table.ForeignKey(
                        name: "FK_plan_risk_action_plan_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "foundation",
                        principalTable: "action_plan",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plan_root_cause",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    content_json = table.Column<string>(type: "text", nullable: true),
                    main_cause = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_root_cause", x => x.id);
                    table.ForeignKey(
                        name: "FK_plan_root_cause_action_plan_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "foundation",
                        principalTable: "action_plan",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_action_item_plan_id",
                schema: "foundation",
                table: "action_item",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_action_plan_auto_key",
                schema: "foundation",
                table: "action_plan",
                column: "auto_key");

            migrationBuilder.CreateIndex(
                name: "IX_action_plan_code",
                schema: "foundation",
                table: "action_plan",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_action_plan_cycle_id",
                schema: "foundation",
                table: "action_plan",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "IX_action_plan_life",
                schema: "foundation",
                table: "action_plan",
                column: "life");

            migrationBuilder.CreateIndex(
                name: "IX_action_plan_responsible_plan_id_user_id",
                schema: "foundation",
                table: "action_plan_responsible",
                columns: new[] { "plan_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_action_plan_responsible_user_id",
                schema: "foundation",
                table: "action_plan_responsible",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_lesson_plan_id",
                schema: "foundation",
                table: "plan_lesson",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_risk_plan_id",
                schema: "foundation",
                table: "plan_risk",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_root_cause_plan_id",
                schema: "foundation",
                table: "plan_root_cause",
                column: "plan_id");

            // ---- as ações que já existem ganham um plano -------------------------
            //
            // Toda ação passa a viver dentro de um plano. As que já estavam no banco nasceram
            // soltas, então cada uma ganha o seu — nada se perde e nada se funde. O plano
            // reaproveita o UUID da própria ação como chave: são tabelas diferentes, a
            // correlação fica trivial e não é preciso inventar uma coluna de rastreio.
            //
            // Os códigos saem em AP-ano-001..N pela ordem de criação, e é de onde a numeração
            // dos próximos continua.
            migrationBuilder.Sql("""
                INSERT INTO foundation.action_plan (
                    id, code, title, description, cost_center, priority, completion,
                    problem, criticality, complexity,
                    roi_expected, saving_expected, saving_realized,
                    investment_planned, investment_actual, cancelled, life,
                    start_date, due_date, created_by, created_by_label, created_at, updated_at, version)
                SELECT
                    i.id,
                    'AP-' || to_char(i.created_at, 'YYYY') || '-' ||
                        lpad((row_number() OVER (
                            PARTITION BY to_char(i.created_at, 'YYYY') ORDER BY i.created_at, i.id
                        ))::text, 3, '0'),
                    i.title,
                    'Plano criado na migração: esta ação existia antes de o plano existir.',
                    i.cost_center, 'MEDIA', 0,
                    i.reason, 'MEDIA', 'MEDIA',
                    0, 0, 0, 0, 0, false, 'ATIVO',
                    i.start_date, i.due_date,
                    i.created_by, i.created_by_label, i.created_at, i.updated_at, 1
                FROM foundation.action_item i;
                """);

            migrationBuilder.Sql(
                "UPDATE foundation.action_item SET plan_id = id WHERE plan_id = '00000000-0000-0000-0000-000000000000';");

            migrationBuilder.AddForeignKey(
                name: "FK_action_item_action_plan_plan_id",
                schema: "foundation",
                table: "action_item",
                column: "plan_id",
                principalSchema: "foundation",
                principalTable: "action_plan",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_action_item_action_plan_plan_id",
                schema: "foundation",
                table: "action_item");

            migrationBuilder.DropTable(
                name: "action_plan_responsible",
                schema: "foundation");

            migrationBuilder.DropTable(
                name: "plan_lesson",
                schema: "foundation");

            migrationBuilder.DropTable(
                name: "plan_risk",
                schema: "foundation");

            migrationBuilder.DropTable(
                name: "plan_root_cause",
                schema: "foundation");

            migrationBuilder.DropTable(
                name: "action_plan",
                schema: "foundation");

            migrationBuilder.DropIndex(
                name: "IX_action_item_plan_id",
                schema: "foundation",
                table: "action_item");

            migrationBuilder.DropColumn(
                name: "comments",
                schema: "foundation",
                table: "action_item");

            migrationBuilder.DropColumn(
                name: "complexity",
                schema: "foundation",
                table: "action_item");

            migrationBuilder.DropColumn(
                name: "dependencies",
                schema: "foundation",
                table: "action_item");

            migrationBuilder.DropColumn(
                name: "evidence",
                schema: "foundation",
                table: "action_item");

            migrationBuilder.DropColumn(
                name: "plan_id",
                schema: "foundation",
                table: "action_item");

            migrationBuilder.DropColumn(
                name: "risk_level",
                schema: "foundation",
                table: "action_item");

            migrationBuilder.DropColumn(
                name: "seq",
                schema: "foundation",
                table: "action_item");

            migrationBuilder.DropColumn(
                name: "support_area",
                schema: "foundation",
                table: "action_item");

            migrationBuilder.AddColumn<Guid>(
                name: "cycle_id",
                schema: "foundation",
                table: "action_item",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_action_item_cycle_id",
                schema: "foundation",
                table: "action_item",
                column: "cycle_id");
        }
    }
}
