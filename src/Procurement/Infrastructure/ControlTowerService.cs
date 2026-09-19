using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.Procurement.Application;
using TrinoSupply.Procurement.Domain;
using TrinoSupply.Procurement.Infrastructure.Persistence;

namespace TrinoSupply.Procurement.Infrastructure;

/// <summary>
/// Monta a leitura da Torre: uma passada por cada fonte (solicitações da janela, cotações vivas,
/// OCs, recebimentos, notas) e o cruzamento em memória por linha da solicitação. As regras de
/// etapa e farol moram em <see cref="ControlTower"/>.
/// </summary>
public sealed class ControlTowerService(ProcurementDbContext db, IClock clock) : IControlTowerService
{
    public async Task<ControlTowerView> ListAsync(ControlTowerFilter filter, CancellationToken ct = default)
    {
        var hoje = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var desde = clock.UtcNow.AddDays(-Math.Clamp(filter.Days, 1, 730));

        var reqs = await db.Requisitions.AsNoTracking().Include(r => r.Lines)
            .Where(r => r.CreatedAt >= desde)
            .OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        if (reqs.Count == 0) return new ControlTowerView(new ControlTowerSummary(0, 0, 0, 0, null, 0m), []);

        var reqIds = reqs.Select(r => r.Id).ToList();
        var centros = (await db.CostCenters.AsNoTracking().ToListAsync(ct)).ToDictionary(c => c.Id);
        var fornecedores = (await db.Suppliers.AsNoTracking().ToListAsync(ct)).ToDictionary(s => s.Id);

        var orders = await db.Orders.AsNoTracking().Include(o => o.Lines)
            .Where(o => reqIds.Contains(o.RequisitionId) && o.Status != PurchaseOrderStatus.Cancelled)
            .ToListAsync(ct);
        var ordersById = orders.ToDictionary(o => o.Id.Value);
        var orderIds = orders.Select(o => o.Id).ToList();

        // Recebido líquido e data da última entrega, por linha da OC.
        var receipts = await db.GoodsReceipts.AsNoTracking().Include(r => r.Lines)
            .Where(r => orderIds.Contains(r.PurchaseOrderId)).ToListAsync(ct);
        var recebidoPorLinhaOc = receipts.SelectMany(r => r.Lines.Select(l => (r.ReceivedAt, l)))
            .GroupBy(x => x.l.OrderLineId)
            .ToDictionary(g => g.Key, g => (Net: g.Sum(x => x.l.NetQuantity), Last: g.Max(x => x.ReceivedAt)));

        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => orderIds.Contains(i.PurchaseOrderId)).ToListAsync(ct);
        var notasPorOc = invoices.GroupBy(i => i.PurchaseOrderId.Value).ToDictionary(g => g.Key, g => g.ToList());

        var emCotacao = (await db.Quotations.AsNoTracking().Include(q => q.Lines)
                .Where(q => q.Status == QuotationStatus.Open || q.Status == QuotationStatus.PartiallyAwarded)
                .ToListAsync(ct))
            .SelectMany(q => q.Lines).Where(l => !l.IsAwarded).Select(l => l.RequisitionLineId).ToHashSet();

        var rows = new List<ControlTowerRow>();
        var leadTimes = new List<double>();

        foreach (var req in reqs)
        {
            centros.TryGetValue(req.CostCenterId, out var centro);

            foreach (var line in req.Lines)
            {
                PurchaseOrder? order = null;
                OrderLine? orderLine = null;
                if (line.PurchaseOrderId is { } ocId && ordersById.TryGetValue(ocId, out order))
                    orderLine = order.Lines.FirstOrDefault(l => l.ItemCode == line.ItemCode);

                var recebido = orderLine is not null && recebidoPorLinhaOc.TryGetValue(orderLine.Id, out var rec)
                    ? rec : (Net: 0m, Last: (DateTimeOffset?)null);

                var timeline = new ItemTimeline(
                    req.Status, HasOrder: order is not null, InOpenQuotation: emCotacao.Contains(line.Id),
                    QuantityRequested: line.Quantity, QuantityReceivedNet: recebido.Net,
                    NeededBy: req.NeededBy,
                    PromisedDate: orderLine?.DeliveryDate is { } d ? DateOnly.FromDateTime(d.UtcDateTime) : null);

                var stage = ControlTower.Stage(timeline);
                var light = ControlTower.Light(timeline, hoje);

                if (stage == ItemStage.Received && recebido.Last is { } ultima)
                    leadTimes.Add((ultima - req.CreatedAt).TotalDays);

                Supplier? supplier = null;
                if (order is not null) fornecedores.TryGetValue(order.SupplierId, out supplier);

                string? notas = null, veredito = null;
                if (order is not null && notasPorOc.TryGetValue(order.Id.Value, out var nfs))
                {
                    notas = string.Join(", ", nfs.Select(n => n.Number));
                    // O pior veredito manda: uma nota divergente segura o processo inteiro.
                    veredito = nfs.Any(n => n.Status == InvoiceMatchStatus.Divergent) ? "Divergent"
                        : nfs.All(n => n.Status == InvoiceMatchStatus.Matched) ? "Matched" : "Pending";
                }

                rows.Add(new ControlTowerRow(
                    req.Id.Value, line.Id, req.CreatedAt, req.RequesterSubject,
                    centro?.Code ?? string.Empty, centro?.Name ?? string.Empty,
                    line.ItemCode, line.Quantity, line.Unit, req.Priority.ToString(), req.NeededBy,
                    stage.ToString(), ControlTower.IsOpen(stage), light.ToString(),
                    order?.Id.Value, order?.Number, supplier?.Code, supplier?.Name, order?.IssuedBySubject,
                    timeline.PromisedDate, recebido.Net, ControlTower.Pending(timeline),
                    orderLine?.ServiceValue, notas, veredito));
            }
        }

        var filtradas = Aplicar(rows, filter);

        var abertas = rows.Where(r => r.IsOpen).ToList();
        var summary = new ControlTowerSummary(
            OpenItems: abertas.Count,
            LateItems: abertas.Count(r => r.Light == nameof(SlaLight.Red)),
            UrgentItems: abertas.Count(r => r.Priority == nameof(RequisitionPriority.Emergencial)),
            WithoutOrder: abertas.Count(r => r.OrderId is null),
            AvgLeadTimeDays: leadTimes.Count > 0 ? Math.Round(leadTimes.Average(), 1) : null,
            BacklogValue: abertas.Where(r => r.OrderId is not null).Sum(r => r.OrderedValue ?? 0m));

        return new ControlTowerView(summary, filtradas);
    }

    private static List<ControlTowerRow> Aplicar(IEnumerable<ControlTowerRow> rows, ControlTowerFilter f)
    {
        var q = rows;
        if (!string.IsNullOrWhiteSpace(f.CostCenterCode))
            q = q.Where(r => r.CostCenterCode.Equals(f.CostCenterCode.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(f.SupplierCode))
            q = q.Where(r => string.Equals(r.SupplierCode, f.SupplierCode.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(f.Stage))
            q = q.Where(r => r.Stage.Equals(f.Stage.Trim(), StringComparison.OrdinalIgnoreCase));
        if (f.OnlyLate) q = q.Where(r => r.Light == nameof(SlaLight.Red));
        if (f.OnlyUrgent) q = q.Where(r => r.Priority == nameof(RequisitionPriority.Emergencial));
        if (f.OnlyWithoutOrder) q = q.Where(r => r.IsOpen && r.OrderId is null);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(r =>
                r.ItemCode.Contains(s, StringComparison.OrdinalIgnoreCase)
                || r.Requester.Contains(s, StringComparison.OrdinalIgnoreCase)
                || r.CostCenterName.Contains(s, StringComparison.OrdinalIgnoreCase)
                || (r.SupplierName?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.OrderNumber?.ToString().Contains(s) ?? false));
        }
        // Vermelho primeiro, depois amarelo; dentro da cor, o mais antigo — é a fila de trabalho.
        return q.OrderByDescending(r => r.IsOpen).ThenByDescending(r => r.Light == "Red")
            .ThenByDescending(r => r.Light == "Yellow").ThenBy(r => r.CreatedAt).ToList();
    }
}
