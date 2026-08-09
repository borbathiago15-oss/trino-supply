using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_entry",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    target_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entry", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entry_company_id_occurred_at",
                schema: "foundation",
                table: "audit_entry",
                columns: new[] { "company_id", "occurred_at" });

            // --- RLS multi-tenant na trilha (SEC-004). ---
            migrationBuilder.Sql(@"
ALTER TABLE foundation.audit_entry ENABLE ROW LEVEL SECURITY;
ALTER TABLE foundation.audit_entry FORCE  ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON foundation.audit_entry
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");

            // --- Imutabilidade (SEC-001/SEC-002 anti-repúdio): append-only por trigger. ---
            // Defesa em profundidade além do grant (a role da app não recebe UPDATE/DELETE).
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION foundation.audit_no_mutate() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'foundation.audit_entry é append-only (SEC-002): % não é permitido', TG_OP;
END;
$$;");
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_audit_no_mutate
    BEFORE UPDATE OR DELETE ON foundation.audit_entry
    FOR EACH ROW EXECUTE FUNCTION foundation.audit_no_mutate();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_no_mutate ON foundation.audit_entry;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS foundation.audit_no_mutate();");
            migrationBuilder.DropTable(
                name: "audit_entry",
                schema: "foundation");
        }
    }
}
