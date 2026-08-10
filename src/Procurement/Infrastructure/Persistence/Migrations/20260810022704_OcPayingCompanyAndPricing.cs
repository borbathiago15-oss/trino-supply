using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Procurement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OcPayingCompanyAndPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address",
                schema: "procurement",
                table: "supplier",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "city",
                schema: "procurement",
                table: "supplier",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "district",
                schema: "procurement",
                table: "supplier",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "email",
                schema: "procurement",
                table: "supplier",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "payment_method",
                schema: "procurement",
                table: "supplier",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "payment_terms",
                schema: "procurement",
                table: "supplier",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "phone",
                schema: "procurement",
                table: "supplier",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "state",
                schema: "procurement",
                table: "supplier",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "state_registration",
                schema: "procurement",
                table: "supplier",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "zip_code",
                schema: "procurement",
                table: "supplier",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "discount_value",
                schema: "procurement",
                table: "purchase_order",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "freight_terms",
                schema: "procurement",
                table: "purchase_order",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "icms_value",
                schema: "procurement",
                table: "purchase_order",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ipi_value",
                schema: "procurement",
                table: "purchase_order",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "number",
                schema: "procurement",
                table: "purchase_order",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "other_expenses",
                schema: "procurement",
                table: "purchase_order",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "paying_company_id",
                schema: "procurement",
                table: "purchase_order",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "payment_method",
                schema: "procurement",
                table: "purchase_order",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "payment_terms",
                schema: "procurement",
                table: "purchase_order",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delivery_date",
                schema: "procurement",
                table: "order_line",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "procurement",
                table: "order_line",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "irrf_percent",
                schema: "procurement",
                table: "order_line",
                type: "numeric(9,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "iss_percent",
                schema: "procurement",
                table: "order_line",
                type: "numeric(9,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_price",
                schema: "procurement",
                table: "order_line",
                type: "numeric(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "paying_company",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tax_id = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    state_registration = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    district = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    city = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    zip_code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_paying_company", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_company_id_number",
                schema: "procurement",
                table: "purchase_order",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_paying_company_company_id_code",
                schema: "procurement",
                table: "paying_company",
                columns: new[] { "company_id", "code" },
                unique: true);

            // --- RLS multi-tenant (SEC-004): a nova tabela de cadastro também é isolada por tenant. ---
            migrationBuilder.Sql(@"
ALTER TABLE procurement.paying_company ENABLE ROW LEVEL SECURITY;
ALTER TABLE procurement.paying_company FORCE  ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON procurement.paying_company
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "paying_company",
                schema: "procurement");

            migrationBuilder.DropIndex(
                name: "IX_purchase_order_company_id_number",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "address",
                schema: "procurement",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "city",
                schema: "procurement",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "district",
                schema: "procurement",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "email",
                schema: "procurement",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "payment_method",
                schema: "procurement",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "payment_terms",
                schema: "procurement",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "phone",
                schema: "procurement",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "state",
                schema: "procurement",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "state_registration",
                schema: "procurement",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "zip_code",
                schema: "procurement",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "discount_value",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "freight_terms",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "icms_value",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "ipi_value",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "number",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "other_expenses",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "paying_company_id",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "payment_method",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "payment_terms",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "delivery_date",
                schema: "procurement",
                table: "order_line");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "procurement",
                table: "order_line");

            migrationBuilder.DropColumn(
                name: "irrf_percent",
                schema: "procurement",
                table: "order_line");

            migrationBuilder.DropColumn(
                name: "iss_percent",
                schema: "procurement",
                table: "order_line");

            migrationBuilder.DropColumn(
                name: "unit_price",
                schema: "procurement",
                table: "order_line");
        }
    }
}
