using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // RLS no Outbox (SEC-004 — fecha a lacuna do diagnóstico de go-live): a role da API
            // (trino_app) só enxerga/escreve eventos do PRÓPRIO tenant (GUC app.current_company).
            // O RELAY precisa drenar TODOS os tenants: a role dedicada `trino_worker` passa pela
            // policy via current_user (sem BYPASSRLS). Superusers (migrations/testes) sempre passam.
            migrationBuilder.Sql(@"
ALTER TABLE foundation.outbox ENABLE ROW LEVEL SECURITY;
ALTER TABLE foundation.outbox FORCE  ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON foundation.outbox
    USING      (company_id = foundation.current_company() OR current_user = 'trino_worker')
    WITH CHECK (company_id = foundation.current_company() OR current_user = 'trino_worker');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON foundation.outbox;
ALTER TABLE foundation.outbox NO FORCE ROW LEVEL SECURITY;
ALTER TABLE foundation.outbox DISABLE ROW LEVEL SECURITY;");
        }
    }
}
