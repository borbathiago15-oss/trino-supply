using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PrazoDeAtendimentoPorFamilia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A tabela nasce **vazia**, e isso é o estado certo: linha nenhuma quer dizer
            // "ninguém configurou", e aí vale o PadraoDeDias do serviço. É o contrário do caso
            // do `material_requestable`, onde a coluna precisou nascer marcada para não
            // esvaziar uma tela em uso — ali o vazio apagava dado, aqui o vazio é o padrão.
            migrationBuilder.CreateTable(
                name: "material_fulfillment_sla",
                schema: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    family = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    max_days = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_fulfillment_sla", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_material_fulfillment_sla_family",
                schema: "materials",
                table: "material_fulfillment_sla",
                column: "family",
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "material_fulfillment_sla",
                schema: "materials");
        }
    }
}
