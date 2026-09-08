using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrcamentoSavingPreCadastro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "tax_id",
                schema: "procurement",
                table: "supplier",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(14)",
                oldMaxLength: 14);

            migrationBuilder.AddColumn<decimal>(
                name: "budget_baseline_value",
                schema: "procurement",
                table: "quotation",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "budget_saving",
                schema: "procurement",
                table: "quotation",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "competition_baseline_value",
                schema: "procurement",
                table: "quotation",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "competition_saving",
                schema: "procurement",
                table: "quotation",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "budget",
                schema: "procurement",
                table: "purchase_requisition",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "budget_baseline_value",
                schema: "procurement",
                table: "quotation");

            migrationBuilder.DropColumn(
                name: "budget_saving",
                schema: "procurement",
                table: "quotation");

            migrationBuilder.DropColumn(
                name: "competition_baseline_value",
                schema: "procurement",
                table: "quotation");

            migrationBuilder.DropColumn(
                name: "competition_saving",
                schema: "procurement",
                table: "quotation");

            migrationBuilder.DropColumn(
                name: "budget",
                schema: "procurement",
                table: "purchase_requisition");

            migrationBuilder.AlterColumn<string>(
                name: "tax_id",
                schema: "procurement",
                table: "supplier",
                type: "character varying(14)",
                maxLength: 14,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(14)",
                oldMaxLength: 14,
                oldNullable: true);
        }
    }
}
