using TrinoSupply.Materials.Application;
using TrinoSupply.Procurement.Application;

namespace TrinoSupply.Api.Procurement;

/// <summary>
/// Roteamento pós-aprovação do Pedido unificado (v2): quando o pedido chega a APROVADO (nível 2),
/// verifica o estoque do Almox. Com saldo para TODAS as linhas → baixa em lote atômica + pedido
/// encerrado como "Atendido pelo estoque" (sem OC). Sem saldo (ou item fora do catálogo) → o pedido
/// permanece Aprovado e segue a rota de compra (emissão de OC), como antes.
/// Orquestração no HOST: os bounded contexts (Procurement/Materials) não se referenciam.
/// </summary>
public sealed class StockFulfillment(
    IPurchaseRequisitionService requisitions, IStockService stock, ILogger<StockFulfillment> logger)
{
    /// <summary>Tenta atender pelo estoque. Nunca falha o approve: erro aqui apenas mantém a rota de compra.</summary>
    public async Task<string> TryFulfillAsync(Guid requisitionId, CancellationToken ct = default)
    {
        var res = await requisitions.GetAsync(requisitionId, ct);
        if (res.IsFailure || res.Value.Status != "Approved") return "purchase";
        var req = res.Value;

        // Todas as linhas têm saldo? Item fora do catálogo do Almox conta como "sem estoque" (rota de compra).
        foreach (var line in req.Lines)
        {
            var bal = await stock.GetBalanceAsync(line.ItemCode, ct);
            if (bal.IsFailure || bal.Value.Quantity < line.Quantity) return "purchase";
        }

        var debit = await stock.PostBatchAsync(
            TrinoSupply.Materials.Domain.StockDirection.Out,
            req.Lines.Select(l => (l.ItemCode, l.Quantity)).ToList(),
            $"Pedido aprovado — atendido pelo estoque interno (pedido {requisitionId})",
            req.CostCenterCode, ct);
        if (debit.IsFailure)
        {
            // Corrida (outra saída consumiu o saldo entre a checagem e a baixa): sem baixa parcial —
            // o lote é atômico; o pedido apenas segue a rota de compra.
            logger.LogInformation("Pedido {Id}: baixa em lote não concluída ({Code}) — segue para compra.",
                requisitionId, debit.Error.Code);
            return "purchase";
        }

        var mark = await requisitions.MarkFulfilledFromStockAsync(requisitionId, ct);
        if (mark.IsFailure)
        {
            // Estado raro (baixa feita, marcação falhou): registra alto e sinaliza para operação.
            logger.LogError("Pedido {Id}: baixa efetuada mas marcação falhou ({Code}).", requisitionId, mark.Error.Code);
            return "purchase";
        }
        return "stock";
    }
}
