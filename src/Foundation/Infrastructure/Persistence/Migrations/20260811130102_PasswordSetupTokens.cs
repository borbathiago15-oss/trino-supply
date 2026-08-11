using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PasswordSetupTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "password_setup_token",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_password_setup_token", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_password_setup_token_token_hash",
                schema: "foundation",
                table: "password_setup_token",
                column: "token_hash",
                unique: true);

            // --- RLS multi-tenant (SEC-004): mesma policy fail-closed das demais tabelas. ---
            migrationBuilder.Sql(@"
ALTER TABLE foundation.password_setup_token ENABLE ROW LEVEL SECURITY;
ALTER TABLE foundation.password_setup_token FORCE  ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON foundation.password_setup_token
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "password_setup_token",
                schema: "foundation");
        }
    }
}
