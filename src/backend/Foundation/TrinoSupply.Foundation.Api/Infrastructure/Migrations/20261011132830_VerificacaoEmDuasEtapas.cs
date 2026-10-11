using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VerificacaoEmDuasEtapas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "mfa_enabled_at",
                schema: "foundation",
                table: "app_user",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mfa_recovery_codes",
                schema: "foundation",
                table: "app_user",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "totp_secret",
                schema: "foundation",
                table: "app_user",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "mfa_enabled_at",
                schema: "foundation",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "mfa_recovery_codes",
                schema: "foundation",
                table: "app_user");

            migrationBuilder.DropColumn(
                name: "totp_secret",
                schema: "foundation",
                table: "app_user");
        }
    }
}
