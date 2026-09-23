using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChamadoDeSuporte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "support_ticket",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    subject = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    screen = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    screen_label = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    client_info = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_to_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_to_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_ticket", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "support_ticket_message",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    from_support = table.Column<bool>(type: "boolean", nullable: false),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attachment_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_ticket_message", x => x.id);
                    table.ForeignKey(
                        name: "FK_support_ticket_message_support_ticket_ticket_id",
                        column: x => x.ticket_id,
                        principalSchema: "foundation",
                        principalTable: "support_ticket",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_support_ticket_created_by_id_updated_at",
                schema: "foundation",
                table: "support_ticket",
                columns: new[] { "created_by_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_support_ticket_number",
                schema: "foundation",
                table: "support_ticket",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_support_ticket_status_updated_at",
                schema: "foundation",
                table: "support_ticket",
                columns: new[] { "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_support_ticket_message_ticket_id_created_at",
                schema: "foundation",
                table: "support_ticket_message",
                columns: new[] { "ticket_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "support_ticket_message",
                schema: "foundation");

            migrationBuilder.DropTable(
                name: "support_ticket",
                schema: "foundation");
        }
    }
}
