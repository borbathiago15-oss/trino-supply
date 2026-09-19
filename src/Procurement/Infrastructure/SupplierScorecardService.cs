using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.Procurement.Application;
using TrinoSupply.Procurement.Domain;
using TrinoSupply.Procurement.Infrastructure.Persistence;

namespace TrinoSupply.Procurement.Infrastructure;

/// <summary>
/// Monta os fatos de entrega (OC × recebimento) e delega o cálculo ao <see cref="SupplierScorecard"/>.
/// A unidade de medida é a LINHA da OC: cada item pedido é um compromisso do fornecedor. Entregas
/// parciais da mesma linha são consolidadas — vale o total recebido e a data da última entrega.
/// </summary>
public sealed class SupplierScorecardService(ProcurementDbContext db, IClock clock) : ISupplierScorecardService
{
    public async Task<Result<SupplierScorecardView>> GetAsync(
        Guid supplierId, int months = 12, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == SupplierId.From(supplierId), ct);
        if (supplier is null)
            return Result.Failure<SupplierScorecardView>(
                new Error("purchases.supplier.not_found", "Fornecedor não encontrado."));

        var janela = Math.Clamp(months, 1, 60);
        var fatos = (await FactsAsync(janela, ct)).Where(f => f.SupplierId == supplierId).ToList();

        var nomes = new Dictionary<Guid, (string Code, string Name)>
        {
            [supplierId] = (supplier.Code, supplier.Name),
        };

        var overall = SupplierScorecard.Overall(supplierId, $"{janela}m", fatos);
        var periodos = SupplierScorecard.ByPeriod(fatos);

        return Result.Success(new SupplierScorecardView(
            supplierId, supplier.Code, supplier.Name, janela,
            ToView(overall, nomes), periodos.Select(p => ToView(p, nomes)).ToList()));
    }

    public async Task<IReadOnlyList<SupplierScoreView>> ListAsync(int months = 12, CancellationToken ct = default)
    {
        var janela = Math.Clamp(months, 1, 60);
        var fatos = await FactsAsync(janela, ct);
        if (fatos.Count == 0) return [];

        var ids = fatos.Select(f => f.SupplierId).Distinct().ToList();
        var nomes = (await db.Suppliers.AsNoTracking().ToListAsync(ct))
            .Where(s => ids.Contains(s.Id.Value))
            .ToDictionary(s => s.Id.Value, s => (s.Code, s.Name));

        return fatos.GroupBy(f => f.SupplierId)
            .Select(g => ToView(SupplierScorecard.Overall(g.Key, $"{janela}m", g), nomes))
            .OrderByDescending(v => v.OtifIndex).ThenBy(v => v.SupplierName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Um fato por LINHA DE OC que teve alguma entrega na janela. Só OCs vivas entram — pedido
    /// cancelado não conta contra (nem a favor de) ninguém.
    /// </summary>
    private async Task<IReadOnlyList<DeliveryFact>> FactsAsync(int months, CancellationToken ct)
    {
        var cutoff = clock.UtcNow.AddMonths(-months);

        var recebimentos = await db.GoodsReceipts.AsNoTracking().Include(r => r.Lines)
            .Where(r => r.ReceivedAt >= cutoff)
            .ToListAsync(ct);
        if (recebimentos.Count == 0) return [];

        var orderIds = recebimentos.Select(r => r.PurchaseOrderId).Distinct().ToList();
        var orders = await db.Orders.AsNoTracking().Include(o => o.Lines)
            .Where(o => orderIds.Contains(o.Id) && o.Status != PurchaseOrderStatus.Cancelled)
            .ToListAsync(ct);

        var linhasDaOc = orders.SelectMany(o => o.Lines.Select(l => (Order: o, Line: l)))
            .ToDictionary(x => x.Line.Id);

        // Consolida as entregas por linha da OC (uma linha pode ter vindo em remessas diferentes).
        return recebimentos
            .SelectMany(r => r.Lines.Select(l => (Receipt: r, Line: l)))
            .Where(x => linhasDaOc.ContainsKey(x.Line.OrderLineId))
            .GroupBy(x => x.Line.OrderLineId)
            .Select(g =>
            {
                var (order, orderLine) = linhasDaOc[g.Key];
                var ultima = g.Max(x => x.Receipt.ReceivedAt);
                return new DeliveryFact(
                    SupplierId: order.SupplierId.Value,
                    Period: $"{ultima:yyyy-MM}",
                    QuantityOrdered: orderLine.Quantity,
                    QuantityReceived: g.Sum(x => x.Line.QuantityReceived),
                    QuantityDamaged: g.Sum(x => x.Line.QuantityDamaged),
                    HasOccurrence: g.Any(x => x.Line.Occurrence != ReceiptOccurrence.None),
                    PromisedDate: orderLine.DeliveryDate is null
                        ? null
                        : DateOnly.FromDateTime(orderLine.DeliveryDate.Value.UtcDateTime),
                    LastReceivedAt: DateOnly.FromDateTime(ultima.UtcDateTime));
            })
            .ToList();
    }

    private static SupplierScoreView ToView(SupplierScore s, IReadOnlyDictionary<Guid, (string Code, string Name)> nomes)
    {
        nomes.TryGetValue(s.SupplierId, out var nome);
        return new SupplierScoreView(
            s.SupplierId, nome.Code ?? string.Empty, nome.Name ?? string.Empty, s.Period,
            s.LinesEvaluated, s.LinesWithDeadline, s.LinesWithoutDeadline,
            s.OnTimeRate, s.InFullRate, s.OtifIndex, s.DamageRate, s.Occurrences, s.Tier.ToString());
    }
}
