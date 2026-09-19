using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.BuildingBlocks.Security;
using TrinoSupply.Procurement.Application;
using TrinoSupply.Procurement.Domain;
using TrinoSupply.Procurement.Infrastructure.Nfe;
using TrinoSupply.Procurement.Infrastructure.Persistence;

namespace TrinoSupply.Procurement.Infrastructure;

/// <summary>
/// Ingestão da NF-e e conciliação de três pontas. O cálculo em si mora no domínio
/// (<see cref="ThreeWayMatch"/>); aqui se reúnem os três fatos — OC, nota e doca — e se persiste o
/// veredito.
/// </summary>
public sealed class InvoiceMatchService(
    ProcurementDbContext db, ITenantContext tenant, ICurrentUser currentUser, IClock clock,
    IConfiguration config, TrinoSupply.Foundation.Infrastructure.Audit.IBusinessAudit audit)
    : IInvoiceMatchService
{
    /// <summary>
    /// Tolerâncias configuráveis por implantação (<c>Procurement:Match:*</c>). O padrão é folga
    /// pequena no preço (centavo de arredondamento em nota com 4 casas) e nenhuma na quantidade.
    /// </summary>
    private MatchTolerance Tolerance => new(
        Percent("Procurement:Match:PriceTolerancePercent", MatchTolerance.Default.PricePercent),
        Percent("Procurement:Match:QuantityTolerancePercent", MatchTolerance.Default.QuantityPercent));

    private decimal Percent(string key, decimal fallback) =>
        decimal.TryParse(config[key], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0
            ? v
            : fallback;

    public async Task<Result<InvoiceImported>> ImportAsync(Guid orderId, string xml, CancellationToken ct = default)
    {
        if (!tenant.HasTenant || string.IsNullOrWhiteSpace(currentUser.Subject))
            return Result.Failure<InvoiceImported>(new Error("purchases.no_context", "Requisição sem tenant/usuário."));

        var order = await db.Orders.AsNoTracking().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == PurchaseOrderId.From(orderId), ct);
        if (order is null)
            return Result.Failure<InvoiceImported>(new Error("purchases.order.not_found", "Pedido não encontrado."));
        if (order.Status == PurchaseOrderStatus.Cancelled)
            return Result.Failure<InvoiceImported>(new Error("purchases.invoice.order_cancelled",
                "Não se concilia nota contra uma OC cancelada."));

        var parsed = NfeXmlParser.Parse(xml);
        if (parsed.IsFailure) return Result.Failure<InvoiceImported>(parsed.Error);

        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == order.SupplierId, ct);

        var invoice = PurchaseInvoice.Import(
            tenant.CompanyId, order.Id, parsed.Value, supplier?.TaxId, currentUser.Subject!, clock.UtcNow);
        if (invoice.IsFailure) return Result.Failure<InvoiceImported>(invoice.Error);

        // Mesma chave duas vezes é reimportação do mesmo arquivo — não vira segunda nota.
        var chave = invoice.Value.AccessKey;
        if (await db.Invoices.AsNoTracking().AnyAsync(i => i.AccessKey == chave, ct))
            return Result.Failure<InvoiceImported>(new Error("purchases.invoice.duplicate_key",
                $"A nota de chave {chave} já foi importada."));

        var resultado = await MatchAsync(order, invoice.Value, ct);
        invoice.Value.ApplyMatch(resultado, clock.UtcNow);

        db.Invoices.Add(invoice.Value);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Result.Failure<InvoiceImported>(new Error("purchases.invoice.duplicate_key",
                $"A nota de chave {chave} já foi importada."));
        }

        await audit.RecordAsync("purchases.invoice.imported", "PurchaseInvoice", invoice.Value.Id.Value.ToString(),
            new
            {
                order = order.Number, accessKey = chave, numero = invoice.Value.Number,
                status = invoice.Value.Status.ToString(), divergencias = resultado.Divergences.Count,
                resumo = resultado.Summary,
            }, ct);

        return Result.Success(Imported(invoice.Value, resultado));
    }

    public async Task<Result<InvoiceImported>> RematchAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await db.Invoices.Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == PurchaseInvoiceId.From(invoiceId), ct);
        if (invoice is null)
            return Result.Failure<InvoiceImported>(new Error("purchases.invoice.not_found", "Nota não encontrada."));

        var order = await db.Orders.AsNoTracking().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == invoice.PurchaseOrderId, ct);
        if (order is null)
            return Result.Failure<InvoiceImported>(new Error("purchases.order.not_found", "Pedido não encontrado."));

        var resultado = await MatchAsync(order, invoice, ct);
        invoice.ApplyMatch(resultado, clock.UtcNow);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<InvoiceImported>(new Error("purchases.conflict",
                "A nota foi alterada concorrentemente. Recarregue."));
        }

        await audit.RecordAsync("purchases.invoice.rematched", "PurchaseInvoice", invoiceId.ToString(),
            new { status = invoice.Status.ToString(), resumo = resultado.Summary }, ct);
        return Result.Success(Imported(invoice, resultado));
    }

    public async Task<Result> ReleaseAsync(Guid invoiceId, string? note, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(currentUser.Subject))
            return Result.Failure(new Error("purchases.no_context", "Requisição sem tenant/usuário."));

        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == PurchaseInvoiceId.From(invoiceId), ct);
        if (invoice is null)
            return Result.Failure(new Error("purchases.invoice.not_found", "Nota não encontrada."));

        var result = invoice.ReleaseWithJustification(currentUser.Subject!, note, clock.UtcNow);
        if (result.IsFailure) return result;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(new Error("purchases.conflict", "A nota foi alterada concorrentemente. Recarregue."));
        }

        // Liberar nota divergente é exceção de controle: vai para a auditoria com nome e motivo.
        await audit.RecordAsync("purchases.invoice.released", "PurchaseInvoice", invoiceId.ToString(),
            new { accessKey = invoice.AccessKey, divergencias = invoice.MatchSummary, justificativa = note }, ct);
        return Result.Success();
    }

    public async Task<Result<OrderMatchView>> GetByOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == PurchaseOrderId.From(orderId), ct);
        if (order is null)
            return Result.Failure<OrderMatchView>(new Error("purchases.order.not_found", "Pedido não encontrado."));

        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == order.SupplierId, ct);
        var invoices = await db.Invoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.PurchaseOrderId == order.Id)
            .OrderByDescending(i => i.ImportedAt).ToListAsync(ct);

        var views = new List<PurchaseInvoiceView>(invoices.Count);
        foreach (var inv in invoices)
            views.Add(ToView(inv, await MatchAsync(order, inv, ct)));

        var tol = Tolerance;
        return Result.Success(new OrderMatchView(
            order.Id.Value, order.Number, supplier?.Code ?? string.Empty, supplier?.Name ?? string.Empty,
            order.Status.ToString(), tol.PricePercent, tol.QuantityPercent, views));
    }

    // ---- apoio ------------------------------------------------------------------------------

    /// <summary>
    /// Reúne as três pontas e delega ao cálculo puro. O físico vem de TODOS os recebimentos da OC
    /// (entrega parcial é normal): o que se compara é o acumulado que chegou, não uma remessa só.
    /// </summary>
    private async Task<MatchResult> MatchAsync(PurchaseOrder order, PurchaseInvoice invoice, CancellationToken ct)
    {
        var receipts = await db.GoodsReceipts.AsNoTracking().Include(r => r.Lines)
            .Where(r => r.PurchaseOrderId == order.Id).ToListAsync(ct);

        var pedido = order.Lines.Select(l => new OrderedFact(l.ItemCode, l.Quantity, l.UnitPrice));
        var nota = invoice.Lines.Select(l => new InvoicedFact(l.ProductCode, l.Quantity, l.UnitPrice));
        var doca = receipts.SelectMany(r => r.Lines)
            .Select(l => new ReceivedFact(l.ItemCode, l.NetQuantity, l.QuantityDamaged));

        return ThreeWayMatch.Run(pedido, nota, doca, Tolerance);
    }

    private static InvoiceImported Imported(PurchaseInvoice inv, MatchResult result) => new(
        inv.Id.Value, inv.AccessKey, inv.Status.ToString(), inv.ReleasedToFinance,
        result.Divergences.Select(ToView).ToList());

    private static MatchDivergenceView ToView(MatchDivergence d) => new(
        d.ItemCode, d.Kind.ToString(), d.Code, d.Expected, d.Found, d.DeviationPercent,
        d.WithinTolerance, d.Message);

    private static PurchaseInvoiceView ToView(PurchaseInvoice inv, MatchResult result) => new(
        inv.Id.Value, inv.PurchaseOrderId.Value, inv.AccessKey, inv.Number, inv.Series, inv.IssuedAt,
        inv.EmitterTaxId, inv.EmitterName, inv.TotalValue, inv.ImportedBySubject, inv.ImportedAt,
        inv.Status.ToString(), inv.MatchedAt, inv.MatchSummary, inv.ReleasedToFinance,
        inv.ReleasedBySubject, inv.ReleasedAt, inv.ReleaseNote,
        inv.Lines.OrderBy(l => l.ItemNumber).Select(l => new InvoiceLineView(
            l.ItemNumber, l.ProductCode, l.Description, l.Ncm, l.Cfop, l.Unit,
            l.Quantity, l.UnitPrice, l.TotalValue)).ToList(),
        result.Lines.Select(l => new MatchLineView(
            l.ItemCode, l.QuantityOrdered, l.UnitPriceOrdered, l.QuantityInvoiced, l.UnitPriceInvoiced,
            l.QuantityReceived, l.QuantityDamaged, l.IsMatched, l.NotInvoiced,
            l.Divergences.Select(ToView).ToList())).ToList());
}
