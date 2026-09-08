using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Comunicados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "announcement",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    image_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    image_file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announcement", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "announcement_dismissal",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    announcement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dismissed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announcement_dismissal", x => x.id);
                    table.ForeignKey(
                        name: "FK_announcement_dismissal_announcement_announcement_id",
                        column: x => x.announcement_id,
                        principalSchema: "foundation",
                        principalTable: "announcement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_announcement_active_starts_on_ends_on",
                schema: "foundation",
                table: "announcement",
                columns: new[] { "active", "starts_on", "ends_on" });

            migrationBuilder.CreateIndex(
                name: "IX_announcement_dismissal_announcement_id_user_id",
                schema: "foundation",
                table: "announcement_dismissal",
                columns: new[] { "announcement_id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "announcement_dismissal",
                schema: "foundation");

            migrationBuilder.DropTable(
                name: "announcement",
                schema: "foundation");
        }
    }
}
