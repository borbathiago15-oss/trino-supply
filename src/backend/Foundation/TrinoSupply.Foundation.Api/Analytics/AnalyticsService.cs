using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Inventory;
using TrinoSupply.Foundation.Api.Materials;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Api.Analytics;

/// <summary>
/// Agregações dos dashboards de Suprimentos e de Estoque. Somente leitura; os cortes
/// gerenciais (regional/gerente/cliente) vêm do cadastro de centros de custo.
/// </summary>
public class AnalyticsService(AppDbContext db, TimeProvider clock)
{
    private const int Cap = 5000; // janela analítica MVP; paginação analítica é evolução

    public static bool CanViewSupply(string role) =>
        role is Roles.Approver or Roles.PurchasingOfficer or Roles.SupplyManager
             or Roles.Director or Roles.Auditor or Roles.SystemAdministrator;

    public async Task<object> SupplyAsync(
        DateOnly from, DateOnly to, Guid? supplierId, Guid? buyerId, Guid? requesterId,
        string? family, string? costCenter, string? region, string? manager, string? client,
        CancellationToken ct = default)
    {
        var fromDt = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toDt = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var ccByCode = (await db.CostCenters.ToListAsync(ct)).ToDictionary(c => c.Code, c => c);
        var familyById = (await db.CatalogItems.Select(i => new { i.Id, i.Family }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Family);

        bool CcMatches(string code)
        {
            if (costCenter is not null && !code.Equals(costCenter, StringComparison.OrdinalIgnoreCase)) return false;
            ccByCode.TryGetValue(code.ToUpperInvariant(), out var cc);
            if (region is not null && !string.Equals(cc?.Region, region, StringComparison.OrdinalIgnoreCase)) return false;
            if (manager is not null && !string.Equals(cc?.ManagerName, manager, StringComparison.OrdinalIgnoreCase)) return false;
            if (client is not null && !string.Equals(cc?.ClientName, client, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        string? FamilyOf(Guid? catalogItemId) =>
            catalogItemId is not null && familyById.TryGetValue(catalogItemId.Value, out var f) ? f : null;

        // ---- pedidos de compra (POs) na janela -----------------------------------
        var pos = await db.PurchaseOrders.Include(o => o.Items)
            .Where(o => o.CreatedAt >= fromDt && o.CreatedAt < toDt)
            .OrderByDescending(o => o.CreatedAt).Take(Cap).ToListAsync(ct);
        var sourcePrIds = pos.Where(o => o.SourcePrId != null).Select(o => o.SourcePrId!.Value).ToList();
        var sourceCcById = (await db.Requisitions
                .Where(r => sourcePrIds.Contains(r.Id))
                .Select(r => new { r.Id, r.CostCenter }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.CostCenter);

        pos = pos.Where(o =>
                (supplierId is null || o.SupplierId == supplierId) &&
                (buyerId is null || o.IssuedBy == buyerId) &&
                (family is null || o.Items.Any(i => string.Equals(FamilyOf(i.CatalogItemId), family, StringComparison.OrdinalIgnoreCase))) &&
                ((costCenter is null && region is null && manager is null && client is null) ||
                 (o.SourcePrId is not null && sourceCcById.TryGetValue(o.SourcePrId.Value, out var cc) && CcMatches(cc))))
            .ToList();

        // ---- requisições (PRs) na janela ----------------------------------------
        async Task<List<PurchaseRequisition>> LoadPrsAsync(DateTimeOffset f, DateTimeOffset t)
        {
            var list = await db.Requisitions.Include(r => r.Items)
                .Where(r => r.CreatedAt >= f && r.CreatedAt < t)
                .OrderByDescending(r => r.CreatedAt).Take(Cap).ToListAsync(ct);
            return list.Where(r =>
                    (requesterId is null || r.RequesterId == requesterId) &&
                    CcMatches(r.CostCenter) &&
                    (family is null || r.Items.Any(i => string.Equals(FamilyOf(i.CatalogItemId), family, StringComparison.OrdinalIgnoreCase))))
                .ToList();
        }
        var prs = await LoadPrsAsync(fromDt, toDt);
        if (supplierId is not null || buyerId is not null)
        {
            var linked = pos.Where(o => o.SourcePrId is not null).Select(o => o.SourcePrId!.Value).ToHashSet();
            prs = prs.Where(r => linked.Contains(r.Id)).ToList();
        }
        var windowDays = toDt - fromDt;
        var prevPrs = await LoadPrsAsync(fromDt - windowDays, fromDt);

        // ---- KPIs ---------------------------------------------------------------
        var approved = prs.Where(r => r.Status == RequisitionStatus.Approved).ToList();
        var approvalDurations = prs
            .Where(r => r.SubmittedAt is not null && r.DecidedAt is not null &&
                        r.Status is RequisitionStatus.Approved or RequisitionStatus.Rejected)
            .Select(r => (r.DecidedAt!.Value - r.SubmittedAt!.Value).TotalDays).ToList();
        var receiveDurations = pos
            .Where(o => o.Status == PurchaseOrderStatus.Received && o.ReceivedAt is not null)
            .Select(o => (o.ReceivedAt!.Value - o.CreatedAt).TotalDays).ToList();
        var lateLimit = clock.GetUtcNow().AddDays(-7);

        var kpis = new
        {
            prCount = prs.Count,
            prPrevCount = prevPrs.Count,
            prTotalValue = prs.Sum(r => r.TotalEstimatedValue),
            approvedCount = approved.Count,
            approvedValue = approved.Sum(r => r.TotalEstimatedValue),
            pendingApproval = prs.Count(r => r.Status is RequisitionStatus.Submitted or RequisitionStatus.InApproval),
            overdue = prs.Count(r => r.NeededBy is not null && r.NeededBy < today &&
                                     r.Status is RequisitionStatus.Submitted or RequisitionStatus.InApproval
                                                 or RequisitionStatus.Approved),
            avgApprovalDays = approvalDurations.Count > 0 ? Math.Round(approvalDurations.Average(), 1) : (double?)null,
            poCount = pos.Count,
            poTotalValue = pos.Where(o => o.Status != PurchaseOrderStatus.Cancelled).Sum(o => o.TotalValue),
            poReceived = pos.Count(o => o.Status == PurchaseOrderStatus.Received),
            poOpen = pos.Count(o => o.Status == PurchaseOrderStatus.Issued),
            poLate = pos.Count(o => o.Status == PurchaseOrderStatus.Issued && o.CreatedAt < lateLimit),
            avgReceiveDays = receiveDurations.Count > 0 ? Math.Round(receiveDurations.Average(), 1) : (double?)null,
        };

        // ---- série mensal -------------------------------------------------------
        var months = new List<object>();
        var cursor = new DateOnly(from.Year, from.Month, 1);
        var end = new DateOnly(to.Year, to.Month, 1);
        while (cursor <= end)
        {
            var m0 = new DateTimeOffset(cursor.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var m1 = new DateTimeOffset(cursor.AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var inMonth = prs.Where(r => r.CreatedAt >= m0 && r.CreatedAt < m1).ToList();
            months.Add(new
            {
                month = cursor.ToString("yyyy-MM"),
                created = inMonth.Count,
                approved = inMonth.Count(r => r.Status == RequisitionStatus.Approved),
                inApproval = inMonth.Count(r => r.Status is RequisitionStatus.Submitted or RequisitionStatus.InApproval),
                returned = inMonth.Count(r => r.Status == RequisitionStatus.Returned),
                rejectedOrCancelled = inMonth.Count(r => r.Status is RequisitionStatus.Rejected or RequisitionStatus.Cancelled),
                draft = inMonth.Count(r => r.Status == RequisitionStatus.Draft),
                poValue = pos.Where(o => o.CreatedAt >= m0 && o.CreatedAt < m1 && o.Status != PurchaseOrderStatus.Cancelled)
                             .Sum(o => o.TotalValue),
            });
            cursor = cursor.AddMonths(1);
        }

        // ---- rankings -----------------------------------------------------------
        static List<object> Rank<T>(IEnumerable<T> src, Func<T, string?> label, Func<T, decimal> value) =>
            src.GroupBy(x => label(x) ?? "—")
               .Select(g => new { label = g.Key, value = g.Sum(value), count = g.Count() })
               .OrderByDescending(x => x.value).Take(10).Cast<object>().ToList();

        string RegionOf(string code) => ccByCode.TryGetValue(code.ToUpperInvariant(), out var c) ? c.Region ?? "SEM REGIONAL" : "SEM REGIONAL";
        string ManagerOf(string code) => ccByCode.TryGetValue(code.ToUpperInvariant(), out var c) ? c.ManagerName ?? "SEM GERENTE" : "SEM GERENTE";
        string ClientOf(string code) => ccByCode.TryGetValue(code.ToUpperInvariant(), out var c) ? c.ClientName ?? "SEM CLIENTE" : "SEM CLIENTE";

        var activePos = pos.Where(o => o.Status != PurchaseOrderStatus.Cancelled).ToList();
        var prItemValues = prs.SelectMany(r => r.Items.Select(i => new
        {
            Family = FamilyOf(i.CatalogItemId) ?? "SEM FAMÍLIA",
            Value = (i.EstimatedUnitPrice ?? 0) * i.Quantity,
        })).ToList();

        var rankings = new
        {
            suppliers = Rank(activePos, o => o.SupplierName, o => o.TotalValue),
            buyers = Rank(activePos, o => o.IssuedByLabel, o => o.TotalValue),
            requesters = Rank(prs, r => r.RequesterLabel, r => r.TotalEstimatedValue),
            families = Rank(prItemValues, x => x.Family, x => x.Value),
            costCenters = Rank(prs, r => r.CostCenter, r => r.TotalEstimatedValue),
            regions = Rank(prs, r => RegionOf(r.CostCenter), r => r.TotalEstimatedValue),
            managers = Rank(prs, r => ManagerOf(r.CostCenter), r => r.TotalEstimatedValue),
            clients = Rank(prs, r => ClientOf(r.CostCenter), r => r.TotalEstimatedValue),
        };

        // ---- tabela fornecedor --------------------------------------------------
        var supplierTable = activePos.GroupBy(o => o.SupplierName)
            .Select(g => new
            {
                supplier = g.Key,
                orders = g.Count(),
                quantity = g.Sum(o => o.Items.Sum(i => i.Quantity)),
                value = g.Sum(o => o.TotalValue),
                open = g.Count(o => o.Status == PurchaseOrderStatus.Issued),
            })
            .OrderByDescending(x => x.value).Take(20).ToList();

        // ---- opções de filtro (para os selects da UI) ---------------------------
        var filterOptions = new
        {
            suppliers = await db.Suppliers.Where(s => s.Active)
                .Select(s => new { id = s.Id, label = s.TradeName ?? s.LegalName }).OrderBy(x => x.label).ToListAsync(ct),
            buyers = await db.PurchaseOrders.Select(o => new { id = o.IssuedBy, label = o.IssuedByLabel })
                .Distinct().OrderBy(x => x.label).Take(200).ToListAsync(ct),
            requesters = await db.Requisitions.Select(r => new { id = r.RequesterId, label = r.RequesterLabel })
                .Distinct().OrderBy(x => x.label).Take(200).ToListAsync(ct),
            families = await db.CatalogItems.Select(i => i.Family).Distinct().OrderBy(f => f).ToListAsync(ct),
            costCenters = ccByCode.Values.Where(c => c.Active).OrderBy(c => c.Code)
                .Select(c => new { code = c.Code, name = c.Name }).ToList(),
            regions = ccByCode.Values.Where(c => c.Region != null).Select(c => c.Region!).Distinct().OrderBy(x => x).ToList(),
            managers = ccByCode.Values.Where(c => c.ManagerName != null).Select(c => c.ManagerName!).Distinct().OrderBy(x => x).ToList(),
            clients = ccByCode.Values.Where(c => c.ClientName != null).Select(c => c.ClientName!).Distinct().OrderBy(x => x).ToList(),
        };

        return new { from, to, kpis, months, rankings, supplierTable, filterOptions };
    }

    /// <summary>
    /// Grade da Solicitação em Lote: por produto — preço, saldo disponível,
    /// previsão de entrada (OCs emitidas), consumo médio mensal (90 dias) e cobertura em dias.
    /// </summary>
    public async Task<object> BatchViewAsync(string? family, string? search, Guid? locationId, CancellationToken ct = default)
    {
        var items = await db.CatalogItems.Where(i => i.Active).OrderBy(i => i.Family).ThenBy(i => i.Description)
            .Take(1000).ToListAsync(ct);
        if (family is not null) items = items.Where(i => string.Equals(i.Family, family, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(search))
            items = items.Where(i => i.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
                                  || i.Code.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

        var balances = await db.StockBalances
            .Where(b => locationId == null || b.LocationId == locationId)
            .GroupBy(b => b.CatalogItemId)
            .Select(g => new { ItemId = g.Key, Available = g.Sum(b => b.TotalQty - b.ReservedQty) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Available, ct);

        var issuedPoIds = await db.PurchaseOrders.Where(o => o.Status == PurchaseOrderStatus.Issued)
            .Select(o => o.Id).ToListAsync(ct);
        var inbound = (await db.PurchaseOrderItems
                .Where(i => i.CatalogItemId != null && issuedPoIds.Contains(i.OrderId)).ToListAsync(ct))
            .GroupBy(i => i.CatalogItemId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var since = clock.GetUtcNow().AddDays(-90);
        var consumption = (await db.StockMovements
                .Where(m => m.Type == MovementType.Issue && m.PerformedAt >= since
                            && (locationId == null || m.LocationId == locationId)).ToListAsync(ct))
            .GroupBy(m => m.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity) / 3m); // média mensal

        return new
        {
            items = items.Select(i =>
            {
                var saldo = balances.GetValueOrDefault(i.Id);
                var consumo = consumption.GetValueOrDefault(i.Id);
                return new
                {
                    id = i.Id, code = i.Code, description = i.Description, family = i.Family,
                    unitOfMeasure = i.UnitOfMeasure, referencePrice = i.ReferencePrice,
                    stockAvailable = saldo,
                    inboundQty = inbound.GetValueOrDefault(i.Id),
                    avgMonthlyConsumption = Math.Round(consumo, 2),
                    coverageDays = consumo > 0 ? (int?)Math.Round(saldo / (consumo / 30m)) : null,
                };
            }),
        };
    }

    public async Task<object> StockAsync(Guid? locationId, string? family, int monthsBack, CancellationToken ct = default)
    {
        monthsBack = Math.Clamp(monthsBack, 1, 24);
        var now = clock.GetUtcNow();
        var start = new DateOnly(now.Year, now.Month, 1).AddMonths(-(monthsBack - 1));
        var startDt = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var items = await db.CatalogItems.ToListAsync(ct);
        if (family is not null) items = items.Where(i => string.Equals(i.Family, family, StringComparison.OrdinalIgnoreCase)).ToList();
        var itemById = items.ToDictionary(i => i.Id, i => i);

        var balances = await db.StockBalances
            .Where(b => locationId == null || b.LocationId == locationId)
            .ToListAsync(ct);
        balances = balances.Where(b => itemById.ContainsKey(b.CatalogItemId)).ToList();
        var locations = await db.StorageLocations.ToListAsync(ct);
        var locById = locations.ToDictionary(l => l.Id, l => l.Code);

        decimal ValueOf(Guid itemId, decimal qty) =>
            (itemById.TryGetValue(itemId, out var i) ? i.ReferencePrice ?? 0 : 0) * qty;

        var totalValue = balances.Sum(b => ValueOf(b.CatalogItemId, b.TotalQty));
        var availableByItem = balances.GroupBy(b => b.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(b => b.TotalQty - b.ReservedQty));
        // reposição: itens de almoxarifado no ou abaixo do estoque mínimo (sem mínimo = quando zera).
        // Enquanto nenhum item estiver marcado como de estoque, considera todos (base ainda sem classificação).
        var stockItems = items.Where(i => i.Active && i.StockControlled).ToList();
        if (stockItems.Count == 0) stockItems = items.Where(i => i.Active).ToList();
        var stockout = stockItems
            .Where(i => availableByItem.GetValueOrDefault(i.Id) <= (i.MinimumQty ?? 0))
            .OrderBy(i => i.Family).ThenBy(i => i.Description).ToList();

        var movements = await db.StockMovements
            .Where(m => m.PerformedAt >= startDt && (locationId == null || m.LocationId == locationId))
            .OrderByDescending(m => m.PerformedAt).Take(Cap).ToListAsync(ct);
        movements = movements.Where(m => itemById.ContainsKey(m.CatalogItemId)).ToList();

        var months = new List<object>();
        var cursor = start;
        var endMonth = new DateOnly(now.Year, now.Month, 1);
        while (cursor <= endMonth)
        {
            var m0 = new DateTimeOffset(cursor.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var m1 = new DateTimeOffset(cursor.AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var inMonth = movements.Where(m => m.PerformedAt >= m0 && m.PerformedAt < m1).ToList();
            months.Add(new
            {
                month = cursor.ToString("yyyy-MM"),
                entriesQty = inMonth.Where(m => m.Type == MovementType.Entry).Sum(m => m.Quantity),
                issuesQty = inMonth.Where(m => m.Type == MovementType.Issue).Sum(m => m.Quantity),
                entries = inMonth.Count(m => m.Type == MovementType.Entry),
                issues = inMonth.Count(m => m.Type == MovementType.Issue),
            });
            cursor = cursor.AddMonths(1);
        }

        var routeItems = await db.MaterialRequisitionItems
            .CountAsync(i => i.Status == MaterialItemStatus.PurchaseRoute, ct);

        var kpis = new
        {
            skusWithBalance = balances.Where(b => b.TotalQty > 0).Select(b => b.CatalogItemId).Distinct().Count(),
            totalQty = balances.Sum(b => b.TotalQty),
            reservedQty = balances.Sum(b => b.ReservedQty),
            totalValue,
            stockoutCount = stockout.Count,
            purchaseRouteItems = routeItems,
            entriesQty = movements.Where(m => m.Type == MovementType.Entry).Sum(m => m.Quantity),
            issuesQty = movements.Where(m => m.Type == MovementType.Issue).Sum(m => m.Quantity),
        };

        var valueByFamily = balances
            .GroupBy(b => itemById[b.CatalogItemId].Family)
            .Select(g => new { label = g.Key, value = g.Sum(b => ValueOf(b.CatalogItemId, b.TotalQty)), count = g.Select(b => b.CatalogItemId).Distinct().Count() })
            .OrderByDescending(x => x.value).Take(10).ToList();
        var topByValue = balances
            .GroupBy(b => b.CatalogItemId)
            .Select(g => new
            {
                label = $"[{itemById[g.Key].Code}] {itemById[g.Key].Description}",
                value = g.Sum(b => ValueOf(g.Key, b.TotalQty)),
                qty = g.Sum(b => b.TotalQty),
            })
            .OrderByDescending(x => x.value).Take(10).ToList();
        var topMovers = movements.Where(m => m.Type == MovementType.Issue)
            .GroupBy(m => m.CatalogItemId)
            .Select(g => new
            {
                label = $"[{itemById[g.Key].Code}] {itemById[g.Key].Description}",
                qty = g.Sum(m => m.Quantity),
                count = g.Count(),
            })
            .OrderByDescending(x => x.qty).Take(10).ToList();

        var stockoutList = stockout.Take(20).Select(i => new
        {
            code = i.Code, description = i.Description, family = i.Family, unit = i.UnitOfMeasure,
            available = availableByItem.GetValueOrDefault(i.Id),
            minimum = i.MinimumQty,
        }).ToList();

        var filterOptions = new
        {
            locations = locations.OrderBy(l => l.Code).Select(l => new { id = l.Id, code = l.Code, name = l.Name }).ToList(),
            families = await db.CatalogItems.Select(i => i.Family).Distinct().OrderBy(f => f).ToListAsync(ct),
        };

        return new { kpis, months, valueByFamily, topByValue, topMovers, stockout = stockoutList, filterOptions };
    }
}
