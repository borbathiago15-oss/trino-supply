using TrinoSupply.Materials.Application;
using TrinoSupply.Materials.Domain;
using TrinoSupply.Procurement.Application;

namespace TrinoSupply.Api.Procurement;

/// <summary>
/// Entrada em estoque do recebimento (MMS-005). Orquestração no HOST: Compras registra a conferência
/// e Materiais credita o saldo — os bounded contexts não se referenciam.
/// Credita a quantidade LÍQUIDA (recebido − avariado). Item que não existe no catálogo do Almox não
/// tem saldo para creditar: fica listado como "não creditado" em vez de derrubar o recebimento, e o
/// crédito pode ser refeito depois (o recebimento continua marcado como pendente de entrada).
/// </summary>
public sealed class ReceiptStockEntry(
    IGoodsReceiptService receipts, IStockService stock, ILogger<ReceiptStockEntry> logger)
{
    public sealed record Outcome(
        bool Posted, IReadOnlyList<string> Credited, IReadOnlyList<string> NotInCatalog, string? Error);

    public async Task<Outcome> PostAsync(Guid receiptId, CancellationToken ct = default)
    {
        var res = await receipts.GetAsync(receiptId, ct);
        if (res.IsFailure) return new Outcome(false, [], [], res.Error.Message);
        var receipt = res.Value;
        if (receipt.StockPosted) return new Outcome(true, [], [], null);

        var liquidas = receipt.Lines
            .Where(l => l.NetQuantity > 0)
            .GroupBy(l => l.ItemCode)
            .Select(g => (ItemCode: g.Key, Quantity: g.Sum(x => x.NetQuantity)))
            .ToList();

        var creditar = new List<(string ItemCode, decimal Quantity)>();
        var foraDoCatalogo = new List<string>();
        foreach (var l in liquidas)
        {
            var saldo = await stock.GetBalanceAsync(l.ItemCode, ct);
            if (saldo.IsFailure) foraDoCatalogo.Add(l.ItemCode);
            else creditar.Add(l);
        }

        if (creditar.Count == 0)
        {
            logger.LogWarning("Recebimento {Id}: nenhum item creditável no Almox ({Itens}).",
                receiptId, string.Join(", ", foraDoCatalogo));
            return new Outcome(false, [], foraDoCatalogo, null);
        }

        var entrada = await stock.PostBatchAsync(
            StockDirection.In, creditar, $"Recebimento NF {receipt.InvoiceNumber}", null, ct);
        if (entrada.IsFailure)
        {
            logger.LogError("Recebimento {Id}: falha ao creditar estoque ({Code}).", receiptId, entrada.Error.Code);
            return new Outcome(false, [], foraDoCatalogo, entrada.Error.Message);
        }

        await receipts.MarkStockPostedAsync(receiptId, ct);
        return new Outcome(true, creditar.Select(c => c.ItemCode).ToList(), foraDoCatalogo, null);
    }
}
