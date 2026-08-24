using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductTypeAndCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.AddColumn<string>(
                name: "product_type",
                schema: "materials",
                table: "catalog_item",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "purchasable",
                schema: "materials",
                table: "catalog_item",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.DropColumn(
                name: "product_type",
                schema: "materials",
                table: "catalog_item");

            migrationBuilder.DropColumn(
                name: "purchasable",
                schema: "materials",
                table: "catalog_item");
        }
    }
}
