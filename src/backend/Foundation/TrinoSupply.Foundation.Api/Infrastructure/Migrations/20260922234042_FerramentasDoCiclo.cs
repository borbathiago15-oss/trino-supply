using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FerramentasDoCiclo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "annual_saving",
                schema: "foundation",
                table: "improvement_cycle",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "leader",
                schema: "foundation",
                table: "improvement_cycle",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mentor",
                schema: "foundation",
                table: "improvement_cycle",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "participants",
                schema: "foundation",
                table: "improvement_cycle",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "improvement_cycle_tool",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tool_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tool_data = table.Column<string>(type: "text", nullable: true),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_improvement_cycle_tool", x => x.id);
                    table.ForeignKey(
                        name: "FK_improvement_cycle_tool_improvement_cycle_cycle_id",
                        column: x => x.cycle_id,
                        principalSchema: "foundation",
                        principalTable: "improvement_cycle",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_improvement_cycle_tool_cycle_id_tool_type",
                schema: "foundation",
                table: "improvement_cycle_tool",
                columns: new[] { "cycle_id", "tool_type" },
                unique: true);

            // ---- a ferramenta que já existia vira a primeira da folha ------------
            //
            // O ciclo tinha uma ferramenta só, em duas colunas. Agora são várias, numa tabela
            // — então o que estava lá entra como a primeira, e nada se perde. Ciclo sem
            // ferramenta preenchida não gera linha: linha vazia diria que a análise existe.
            migrationBuilder.Sql("""
                INSERT INTO foundation.improvement_cycle_tool (id, cycle_id, tool_type, tool_data, seq, updated_at)
                SELECT gen_random_uuid(), c.id, c.tool_name, c.tool_data, 1, c.updated_at
                FROM foundation.improvement_cycle c
                WHERE c.tool_name IS NOT NULL AND c.tool_name <> '';
                """);

            migrationBuilder.DropColumn(
                name: "tool_name",
                schema: "foundation",
                table: "improvement_cycle");

            migrationBuilder.DropColumn(
                name: "tool_data",
                schema: "foundation",
                table: "improvement_cycle");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "improvement_cycle_tool",
                schema: "foundation");

            migrationBuilder.DropColumn(
                name: "annual_saving",
                schema: "foundation",
                table: "improvement_cycle");

            migrationBuilder.DropColumn(
                name: "leader",
                schema: "foundation",
                table: "improvement_cycle");

            migrationBuilder.DropColumn(
                name: "mentor",
                schema: "foundation",
                table: "improvement_cycle");

            migrationBuilder.DropColumn(
                name: "participants",
                schema: "foundation",
                table: "improvement_cycle");

            migrationBuilder.AddColumn<string>(
                name: "tool_data",
                schema: "foundation",
                table: "improvement_cycle",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tool_name",
                schema: "foundation",
                table: "improvement_cycle",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }
    }
}
