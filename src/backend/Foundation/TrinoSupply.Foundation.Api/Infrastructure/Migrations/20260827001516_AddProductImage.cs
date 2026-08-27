using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "image_document_id",
                schema: "materials",
                table: "catalog_item",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "image_file_name",
                schema: "materials",
                table: "catalog_item",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image_document_id",
                schema: "materials",
                table: "catalog_item");

            migrationBuilder.DropColumn(
                name: "image_file_name",
                schema: "materials",
                table: "catalog_item");
        }
    }
}
