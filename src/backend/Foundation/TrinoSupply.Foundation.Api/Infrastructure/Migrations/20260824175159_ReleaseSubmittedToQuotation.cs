using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Foundation.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseSubmittedToQuotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fluxo novo: a SC enviada vai direto para a fila de cotação. As solicitações que
            // estavam retidas na autorização prévia (sem preço) são liberadas para Suprimentos.
            migrationBuilder.Sql(
                "UPDATE procurement.purchase_requisition SET status = 2 WHERE status = 3;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE procurement.purchase_requisition SET status = 3 WHERE status = 2;");
        }
    }
}
