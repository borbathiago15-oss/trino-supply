using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                schema: "foundation",
                table: "app_user",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "refresh_token",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_token", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_token_hash",
                schema: "foundation",
                table: "refresh_token",
                column: "token_hash",
                unique: true);

            // --- RLS multi-tenant nos refresh tokens (SEC-004). ---
            migrationBuilder.Sql(@"
ALTER TABLE foundation.refresh_token ENABLE ROW LEVEL SECURITY;
ALTER TABLE foundation.refresh_token FORCE  ROW LEVEL SECURITY;");
            migrationBuilder.Sql(@"
CREATE POLICY tenant_isolation ON foundation.refresh_token
    USING      (company_id = foundation.current_company())
    WITH CHECK (company_id = foundation.current_company());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refresh_token",
                schema: "foundation");

            migrationBuilder.DropColumn(
                name: "password_hash",
                schema: "foundation",
                table: "app_user");
        }
    }
}
