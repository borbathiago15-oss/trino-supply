using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.BuildingBlocks.Security;
using TrinoSupply.Procurement.Application;
using TrinoSupply.Procurement.Domain;
using TrinoSupply.Procurement.Infrastructure.Persistence;

namespace TrinoSupply.Procurement.Infrastructure;

/// <summary>
/// Recebimento de mercadoria (MMS-005): registra a conferência física contra a OC, acumulando
/// entregas parciais. Devolve ao host a quantidade LÍQUIDA a creditar no Almox.
/// </summary>
public sealed class GoodsReceiptService(
    ProcurementDbContext db, ITenantContext tenant, ICurrentUser currentUser, IUsageMetrics metrics, IClock clock,
    TrinoSupply.Foundation.Infrastructure.Audit.IBusinessAudit audit)
    : IGoodsReceiptService
{
    public async Task<Result<ReceiptRegistered>> RegisterAsync(
        Guid orderId, RegisterReceiptInput input, CancellationToken ct = default)
    {
        if (!tenant.HasTenant || string.IsNullOrWhiteSpace(currentUser.Subject))
            return Result.Failure<ReceiptRegistered>(new Error("purchases.no_context", "Requisição sem tenant/usuário."));

        var order = await db.Orders.Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == PurchaseOrderId.From(orderId), ct);
        if (order is null)
            return Result.Failure<ReceiptRegistered>(new Error("purchases.order.not_found", "Pedido não encontrado."));
        if (order.Status == PurchaseOrderStatus.Cancelled)
            return Result.Failure<ReceiptRegistered>(new Error("purchases.order.cancelled", "OC cancelada não recebe mercadoria."));

        // Entregas anteriores desta OC: quanto já entrou por linha (a entrega parcial é a regra).
        var recebidoAntes = await RecebidoPorLinhaAsync(orderId, ct);

        var pedidas = order.Lines.ToDictionary(l => l.Id);
        var entradas = new List<ReceiptLineInput>();
        foreach (var l in input.Lines ?? [])
        {
            if (!pedidas.TryGetValue(l.OrderLineId, out var linhaOc))
                return Result.Failure<ReceiptRegistered>(new Error("purchases.receipt.line_not_found",
                    "Item informado não pertence a esta OC."));

            var ocorrencia = ParseOccurrence(l.Occurrence);
            if (ocorrencia is null)
                return Result.Failure<ReceiptRegistered>(new Error("purchases.receipt.occurrence_invalid",
                    $"Ocorrência '{l.Occurrence}' inválida. Use Avaria, Falta, Excesso ou Divergencia."));

            entradas.Add(new ReceiptLineInput(
                linhaOc.Id, linhaOc.ItemCode, linhaOc.Unit, linhaOc.Quantity,
                recebidoAntes.GetValueOrDefault(linhaOc.Id), l.QuantityReceived, l.QuantityDamaged,
                ocorrencia.Value, l.OccurrenceNote));
        }

        var criado = GoodsReceipt.Register(
            tenant.CompanyId, order.Id, input.InvoiceNumber, input.InvoiceDate,
            currentUser.Subject!, clock.UtcNow, input.Notes, entradas);
        if (criado.IsFailure) return Result.Failure<ReceiptRegistered>(criado.Error);
        var receipt = criado.Value;

        // A OC fecha quando TODAS as linhas tiverem chegado por completo (somando esta entrega).
        var completa = order.Lines.All(linha =>
        {
            var total = recebidoAntes.GetValueOrDefault(linha.Id)
                        + receipt.Lines.Where(r => r.OrderLineId == linha.Id).Sum(r => r.QuantityReceived);
            return total >= linha.Quantity;
        });
        var progresso = order.MarkReceiptProgress(completa);
        if (progresso.IsFailure) return Result.Failure<ReceiptRegistered>(progresso.Error);

        db.GoodsReceipts.Add(receipt);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<ReceiptRegistered>(new Error("purchases.conflict",
                "A OC foi alterada concorrentemente. Recarregue e tente novamente."));
        }

        metrics.Record("purchases.receipt.registered", tenant.CompanyId.Value.ToString());
        await audit.RecordAsync("purchases.receipt.registered", "GoodsReceipt", receipt.Id.Value.ToString(),
            new
            {
                orderId, orderNumber = order.Number, invoice = receipt.InvoiceNumber,
                itens = receipt.Lines.Select(l => new
                {
                    l.ItemCode, recebido = l.QuantityReceived, avariado = l.QuantityDamaged,
                    liquido = l.NetQuantity, ocorrencia = l.Occurrence.ToString(), l.OccurrenceNote,
                }).ToArray(),
                ocStatus = order.Status.ToString(),
            }, ct);

        // Só o líquido entra no estoque; o avariado fica registrado para a tratativa com o fornecedor.
        var stockLines = receipt.Lines
            .Where(l => l.NetQuantity > 0)
            .GroupBy(l => l.ItemCode)
            .Select(g => new StockEntryLine(g.Key, g.Sum(x => x.NetQuantity)))
            .ToList();

        return Result.Success(new ReceiptRegistered(receipt.Id.Value, stockLines, completa, receipt.HasOccurrence));
    }

    public async Task<Result> MarkStockPostedAsync(Guid receiptId, CancellationToken ct = default)
    {
        var receipt = await db.GoodsReceipts.Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == GoodsReceiptId.From(receiptId), ct);
        if (receipt is null)
            return Result.Failure(new Error("purchases.receipt.not_found", "Recebimento não encontrado."));

        var r = receipt.MarkStockPosted(clock.UtcNow);
        if (r.IsFailure) return r;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("purchases.receipt.stock_posted", "GoodsReceipt", receiptId.ToString(),
            new { itens = receipt.Lines.Select(l => $"{l.ItemCode}x{l.NetQuantity}").ToArray() }, ct);
        return Result.Success();
    }

    public async Task<Result<OrderReceiptSummaryView>> GetOrderReceiptsAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == PurchaseOrderId.From(orderId), ct);
        if (order is null)
            return Result.Failure<OrderReceiptSummaryView>(new Error("purchases.order.not_found", "Pedido não encontrado."));

        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == order.SupplierId, ct);
        var recebidoAntes = await RecebidoPorLinhaAsync(orderId, ct);

        var linhas = order.Lines.Select(l =>
        {
            var ja = recebidoAntes.GetValueOrDefault(l.Id);
            return new PendingReceiptLineView(l.Id, l.ItemCode, l.Description, l.Unit, l.Quantity, ja,
                Math.Max(0m, l.Quantity - ja));
        }).ToList();

        var receipts = await db.GoodsReceipts.AsNoTracking().Include(r => r.Lines)
            .Where(r => r.PurchaseOrderId == order.Id)
            .OrderByDescending(r => r.ReceivedAt)
            .ToListAsync(ct);

        return Result.Success(new OrderReceiptSummaryView(
            order.Id.Value, order.Number, order.Status.ToString(),
            supplier?.Code ?? string.Empty, supplier?.Name ?? string.Empty,
            linhas, receipts.Select(ToView).ToList()));
    }

    public async Task<Result<ReceiptView>> GetAsync(Guid receiptId, CancellationToken ct = default)
    {
        var receipt = await db.GoodsReceipts.AsNoTracking().Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == GoodsReceiptId.From(receiptId), ct);
        return receipt is null
            ? Result.Failure<ReceiptView>(new Error("purchases.receipt.not_found", "Recebimento não encontrado."))
            : Result.Success(ToView(receipt));
    }

    /// <summary>Quanto já foi recebido por linha da OC (soma das entregas anteriores).</summary>
    private async Task<Dictionary<Guid, decimal>> RecebidoPorLinhaAsync(Guid orderId, CancellationToken ct)
    {
        var anteriores = await db.GoodsReceipts.AsNoTracking().Include(r => r.Lines)
            .Where(r => r.PurchaseOrderId == PurchaseOrderId.From(orderId))
            .SelectMany(r => r.Lines)
            .ToListAsync(ct);
        return anteriores.GroupBy(l => l.OrderLineId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.QuantityReceived));
    }

    private static ReceiptOccurrence? ParseOccurrence(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ReceiptOccurrence.None
            : Enum.TryParse<ReceiptOccurrence>(value.Trim(), ignoreCase: true, out var parsed) ? parsed : null;

    private static ReceiptView ToView(GoodsReceipt r) => new(
        r.Id.Value, r.PurchaseOrderId.Value, r.InvoiceNumber, r.InvoiceDate, r.ReceivedBySubject,
        r.ReceivedAt, r.Notes, r.StockPosted,
        r.Lines.Select(l => new ReceiptLineView(
            l.OrderLineId, l.ItemCode, l.Unit, l.QuantityOrdered, l.QuantityReceived, l.QuantityDamaged,
            l.NetQuantity, l.Occurrence.ToString(), l.OccurrenceNote)).ToList());
}
