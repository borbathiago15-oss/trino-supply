using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveCaToSupplierAndDropFispq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) o C.A. passa a ser do par produto+fornecedor
            migrationBuilder.AddColumn<string>(
                name: "ca_number",
                schema: "materials",
                table: "catalog_item_supplier",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            // 2) o C.A. que estava no produto vai para os fornecedores já cadastrados dele
            migrationBuilder.Sql("""
                UPDATE materials.catalog_item_supplier s
                   SET ca_number = i.ca_number
                  FROM materials.catalog_item i
                 WHERE s.catalog_item_id = i.id
                   AND i.ca_number IS NOT NULL AND i.ca_number <> ''
                   AND (s.ca_number IS NULL OR s.ca_number = '');
                """);

            // 3) e some do produto, junto com a FISPQ (o estoque físico de químicos não é tratado aqui)
            migrationBuilder.DropColumn(
                name: "ca_number",
                schema: "materials",
                table: "catalog_item");

            migrationBuilder.DropColumn(
                name: "fispq_document_id",
                schema: "materials",
                table: "catalog_item");

            migrationBuilder.DropColumn(
                name: "fispq_file_name",
                schema: "materials",
                table: "catalog_item");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ca_number",
                schema: "materials",
                table: "catalog_item_supplier");

            migrationBuilder.AddColumn<string>(
                name: "ca_number",
                schema: "materials",
                table: "catalog_item",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "fispq_document_id",
                schema: "materials",
                table: "catalog_item",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fispq_file_name",
                schema: "materials",
                table: "catalog_item",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }
    }
}
