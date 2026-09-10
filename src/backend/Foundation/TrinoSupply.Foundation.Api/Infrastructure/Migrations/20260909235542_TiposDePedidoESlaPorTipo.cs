using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TiposDePedidoESlaPorTipo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stage_sla_stage",
                schema: "procurement",
                table: "stage_sla");

            migrationBuilder.AddColumn<string>(
                name: "request_type",
                schema: "procurement",
                table: "stage_sla",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "request_type",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_request_type", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stage_sla_request_type_stage",
                schema: "procurement",
                table: "stage_sla",
                columns: new[] { "request_type", "stage" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_request_type_code",
                schema: "procurement",
                table: "request_type",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "request_type",
                schema: "procurement");

            migrationBuilder.DropIndex(
                name: "IX_stage_sla_request_type_stage",
                schema: "procurement",
                table: "stage_sla");

            migrationBuilder.DropColumn(
                name: "request_type",
                schema: "procurement",
                table: "stage_sla");

            migrationBuilder.CreateIndex(
                name: "IX_stage_sla_stage",
                schema: "procurement",
                table: "stage_sla",
                column: "stage",
                unique: true);
        }
    }
}
