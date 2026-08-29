using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierHomologationAndDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "homologation_status",
                schema: "procurement",
                table: "supplier",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "HOMOLOGADO");

            migrationBuilder.CreateTable(
                name: "supplier_document",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: true),
                    uploaded_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_document", x => x.id);
                    table.ForeignKey(
                        name: "FK_supplier_document_supplier_supplier_id",
                        column: x => x.supplier_id,
                        principalSchema: "procurement",
                        principalTable: "supplier",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_document_supplier_id_type",
                schema: "procurement",
                table: "supplier_document",
                columns: new[] { "supplier_id", "type" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_document_valid_until",
                schema: "procurement",
                table: "supplier_document",
                column: "valid_until");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_document",
                schema: "procurement");

            migrationBuilder.DropColumn(
                name: "homologation_status",
                schema: "procurement",
                table: "supplier");
        }
    }
}
