using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "foundation");

            migrationBuilder.CreateTable(
                name: "app_user",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    role_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "company",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tax_id = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    aggregate_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "role",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    permissions = table.Column<string[]>(type: "text[]", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_user_company_id_email",
                schema: "foundation",
                table: "app_user",
                columns: new[] { "company_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_app_user_company_id_subject",
                schema: "foundation",
                table: "app_user",
                columns: new[] { "company_id", "subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_tax_id",
                schema: "foundation",
                table: "company",
                column: "tax_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_published_at",
                schema: "foundation",
                table: "outbox",
                column: "published_at");

            migrationBuilder.CreateIndex(
                name: "IX_role_company_id_name",
                schema: "foundation",
                table: "role",
                columns: new[] { "company_id", "name" },
                unique: true);

            // --- Índices operacionais do Outbox (ARC-005 §3) que o EF não gera do modelo. ---
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_outbox_pending ON foundation.outbox (occurred_at) WHERE published_at IS NULL;");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_outbox_company ON foundation.outbox (company_id);");

            // --- RLS multi-tenant (SEC-004). Não é gerado pelo modelo EF; aplicado aqui. ---
            // Tenant corrente da sessão (definido por conexão pelo TenantConnectionInterceptor).
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION foundation.current_company() RETURNS uuid
LANGUAGE sql STABLE AS $$
    SELECT NULLIF(current_setting('app.current_company', true), '')::uuid
$$;");

            // Tabelas de negócio recebem RLS; company/outbox NÃO (infra/catálogo — SEC-004 §6).
            migrationBuilder.Sql(@"
ALTER TABLE foundation.role     ENABLE ROW LEVEL SECURITY;
ALTER TABLE foundation.role     FORCE  ROW LEVEL SECURITY;
ALTER TABLE foundation.app_user ENABLE ROW LEVEL SECURITY;
ALTER TABLE foundation.app_user FORCE  ROW LEVEL SECURITY;");

            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON foundation.role
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");

            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON foundation.app_user
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_user",
                schema: "foundation");

            migrationBuilder.DropTable(
                name: "company",
                schema: "foundation");

            migrationBuilder.DropTable(
                name: "outbox",
                schema: "foundation");

            migrationBuilder.DropTable(
                name: "role",
                schema: "foundation");

            // Policies caem junto com as tabelas; a função é dropada por último.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS foundation.current_company();");
        }
    }
}
