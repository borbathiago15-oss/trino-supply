using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrinoSupply.Procurement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SplitPurchaseOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_purchase_order_company_id_requisition_id",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.AddColumn<Guid>(
                name: "purchase_order_id",
                schema: "procurement",
                table: "requisition_line",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_requisition_line_purchase_order_id",
                schema: "procurement",
                table: "requisition_line",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_company_id_requisition_id",
                schema: "procurement",
                table: "purchase_order",
                columns: new[] { "company_id", "requisition_id" });

            // --- Backfill dos dados existentes ---------------------------------------------------
            // Antes da compra dividida, UMA OC cobria a requisição INTEIRA. Sem este backfill, as
            // requisições já compradas ficariam com todas as linhas "a pedir" e aceitariam uma
            // segunda OC para itens que já foram adquiridos.
            // Roda com o papel das migrations (superuser/owner no bootstrap), que não é filtrado por RLS.
            migrationBuilder.Sql(@"
UPDATE procurement.requisition_line rl
   SET purchase_order_id = po.id
  FROM procurement.purchase_order po
 WHERE po.requisition_id = rl.requisition_id
   AND po.company_id    = rl.company_id
   AND po.status        = 1            -- Issued (OC cancelada não prende a linha)
   AND rl.purchase_order_id IS NULL;");

            // Requisição com todas as linhas cobertas passa de Approved(4) para Ordered(8).
            migrationBuilder.Sql(@"
UPDATE procurement.purchase_requisition r
   SET status = 8
 WHERE r.status = 4
   AND EXISTS     (SELECT 1 FROM procurement.requisition_line l WHERE l.requisition_id = r.id)
   AND NOT EXISTS (SELECT 1 FROM procurement.requisition_line l
                    WHERE l.requisition_id = r.id AND l.purchase_order_id IS NULL);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Os status novos não existem no enum antigo: devolve as requisições para Approved.
            migrationBuilder.Sql(
                "UPDATE procurement.purchase_requisition SET status = 4 WHERE status IN (7, 8);");

            migrationBuilder.DropIndex(
                name: "IX_requisition_line_purchase_order_id",
                schema: "procurement",
                table: "requisition_line");

            migrationBuilder.DropIndex(
                name: "IX_purchase_order_company_id_requisition_id",
                schema: "procurement",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "purchase_order_id",
                schema: "procurement",
                table: "requisition_line");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_company_id_requisition_id",
                schema: "procurement",
                table: "purchase_order",
                columns: new[] { "company_id", "requisition_id" },
                unique: true,
                filter: "status = 1");
        }
    }
}
