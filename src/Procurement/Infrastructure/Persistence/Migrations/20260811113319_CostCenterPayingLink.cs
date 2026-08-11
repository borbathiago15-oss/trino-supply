using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Procurement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CostCenterPayingLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "paying_company_id",
                schema: "procurement",
                table: "cost_center",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "paying_company_id",
                schema: "procurement",
                table: "cost_center");
        }
    }
}
