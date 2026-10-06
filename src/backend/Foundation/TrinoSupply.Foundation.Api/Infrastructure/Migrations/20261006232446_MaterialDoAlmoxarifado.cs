using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MaterialDoAlmoxarifado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // `true`, e não o `false` que o EF escreve sozinho: esta coluna chega a uma tela
            // **em uso**. Nascendo `false`, toda família do cadastro ficaria fora de Solicitar
            // Material no instante do deploy, e a tela esvaziaria para todo mundo sem ninguém
            // ter decidido nada. Quem tira um grupo de circulação é o cadastro, não a migration.
            migrationBuilder.AddColumn<bool>(
                name: "material_requestable",
                schema: "materials",
                table: "product_family",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "material_requestable",
                schema: "materials",
                table: "catalog_item",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "material_requestable",
                schema: "materials",
                table: "product_family");

            migrationBuilder.DropColumn(
                name: "material_requestable",
                schema: "materials",
                table: "catalog_item");
        }
    }
}
