using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.Procurement.Application;
using TrinoSupply.Procurement.Domain;
using TrinoSupply.Procurement.Infrastructure.Persistence;

namespace TrinoSupply.Procurement.Infrastructure;

/// <summary>
/// Reúne os fatos de governança de cada processo de compra (solicitação → cotação → OC) e delega a
/// pontuação ao <see cref="ComplianceScore"/>. Nenhum fato é inferido: todos saem de registros.
/// </summary>
public sealed class ComplianceService(ProcurementDbContext db, IClock clock) : IComplianceService
{
    public async Task<Result<ComplianceView>> GetAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == PurchaseOrderId.From(orderId), ct);
        if (order is null)
            return Result.Failure<ComplianceView>(new Error("purchases.order.not_found", "Pedido não encontrado."));

        var req = await db.Requisitions.AsNoTracking().Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == order.RequisitionId, ct);
        if (req is null)
            return Result.Failure<ComplianceView>(new Error("purchases.not_found", "Requisição não encontrada."));

        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == order.SupplierId, ct);
        var quotation = await CotacaoDaAsync(req.Id, ct);

        return Result.Success(Avaliar(order, req, supplier, quotation));
    }

    public async Task<IReadOnlyList<ComplianceView>> ListAsync(
        int? maxScore = null, int limit = 200, CancellationToken ct = default)
    {
        var orders = await db.Orders.AsNoTracking().Include(o => o.Lines)
            .Where(o => o.Status != PurchaseOrderStatus.Cancelled)
            .OrderByDescending(o => o.Number).Take(Math.Clamp(limit, 1, 1000)).ToListAsync(ct);
        if (orders.Count == 0) return [];

        var reqIds = orders.Select(o => o.RequisitionId).Distinct().ToList();
        var reqs = (await db.Requisitions.AsNoTracking().Include(r => r.Lines)
            .Where(r => reqIds.Contains(r.Id)).ToListAsync(ct)).ToDictionary(r => r.Id);
        var suppliers = (await db.Suppliers.AsNoTracking().ToListAsync(ct)).ToDictionary(s => s.Id);

        // Uma leitura só das cotações vivas da janela, indexada por linha de requisição.
        var quotations = await db.Quotations.AsNoTracking()
            .Include(q => q.Lines).Include(q => q.Participants).Include(q => q.Bids)
            .Where(q => q.Status != QuotationStatus.Cancelled)
            .ToListAsync(ct);

        var views = new List<ComplianceView>(orders.Count);
        foreach (var order in orders)
        {
            if (!reqs.TryGetValue(order.RequisitionId, out var req)) continue;
            var quotation = quotations.FirstOrDefault(q => q.RequisitionId == req.Id);
            views.Add(Avaliar(order, req, suppliers.GetValueOrDefault(order.SupplierId), quotation));
        }

        return views
            .Where(v => maxScore is null || v.Score <= maxScore)
            .OrderBy(v => v.Score).ThenByDescending(v => v.IssuedAt)
            .ToList();
    }

    private async Task<Quotation?> CotacaoDaAsync(RequisitionId requisitionId, CancellationToken ct) =>
        await db.Quotations.AsNoTracking()
            .Include(q => q.Lines).Include(q => q.Participants).Include(q => q.Bids)
            .FirstOrDefaultAsync(q => q.RequisitionId == requisitionId && q.Status != QuotationStatus.Cancelled, ct);

    private ComplianceView Avaliar(
        PurchaseOrder order, PurchaseRequisition req, Supplier? supplier, Quotation? quotation)
    {
        var homologado = supplier is { Status: SupplierStatus.Active };

        // −1 distingue "não houve cotação" (compra direta) de "houve, mas ninguém respondeu".
        var respostas = quotation?.ResponseCount ?? -1;

        var (foraDoMenor, justificada) = AnalisarAdjudicacao(quotation, order.SupplierId.Value);

        var facts = new ComplianceFacts(
            Emergencial: req.Priority == RequisitionPriority.Emergencial,
            NeededBy: req.NeededBy,
            CreatedOn: DateOnly.FromDateTime(req.CreatedAt.UtcDateTime),
            SupplierHomologated: homologado,
            QuotationResponses: respostas,
            AwardedOutsideLowest: foraDoMenor,
            AwardJustified: justificada);

        var resultado = ComplianceScore.Evaluate(facts);

        return new ComplianceView(
            order.Id.Value, order.Number, req.Id.Value,
            supplier?.Code ?? string.Empty, supplier?.Name ?? string.Empty,
            order.IssuedAt, order.NetValue, resultado.Score, resultado.Band, resultado.Summary,
            facts.Emergencial, facts.NeededBy, facts.CreatedOn, homologado, respostas,
            foraDoMenor, justificada,
            resultado.Penalties.Select(p => new CompliancePenaltyView(
                p.Rule.ToString(), p.Points, p.Title, p.Evidence)).ToList());
    }

    /// <summary>
    /// Olha as linhas que este fornecedor ganhou: alguma saiu por preço acima do menor recebido? E,
    /// se saiu, o comprador registrou o porquê? (O domínio da cotação já exige a justificativa na
    /// adjudicação — esta regra é a rede de segurança, e cobre dado anterior a essa exigência.)
    /// </summary>
    private static (bool ForaDoMenor, bool Justificada) AnalisarAdjudicacao(Quotation? quotation, Guid supplierId)
    {
        if (quotation is null) return (false, true);

        var ganhas = quotation.Lines.Where(l => l.AwardedSupplierId == supplierId).ToList();
        if (ganhas.Count == 0) return (false, true);

        var foraDoMenor = false;
        var todasJustificadas = true;
        foreach (var linha in ganhas)
        {
            var doItem = quotation.Bids.Where(b => b.LineId == linha.Id).ToList();
            if (doItem.Count == 0) continue;

            var menor = doItem.Min(b => b.UnitPrice);
            if (linha.AwardedUnitPrice is not { } preco || preco <= menor) continue;

            foraDoMenor = true;
            if (string.IsNullOrWhiteSpace(linha.AwardNote)) todasJustificadas = false;
        }

        return (foraDoMenor, todasJustificadas);
    }
}
