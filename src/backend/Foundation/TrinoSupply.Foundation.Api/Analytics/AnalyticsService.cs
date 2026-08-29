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

        // categoria (V2-P3): agrupador de famílias — o spend real das O.C.s consolidado por categoria
        var categoryByFamily = (await db.ProductFamilies
                .Select(f => new { f.Name, f.Category }).ToListAsync(ct))
            .ToDictionary(x => x.Name, x => x.Category, StringComparer.OrdinalIgnoreCase);
        string CategoryOf(string familyName) =>
            categoryByFamily.TryGetValue(familyName, out var c) && !string.IsNullOrWhiteSpace(c)
                ? c! : "SEM CATEGORIA";
        var poItemValues = activePos.SelectMany(o => o.Items.Select(i => new
        {
            Family = FamilyOf(i.CatalogItemId) ?? "SEM FAMÍLIA",
            Value = (i.UnitPrice ?? 0) * i.Quantity,
        })).ToList();

        var rankings = new
        {
            suppliers = Rank(activePos, o => o.SupplierName, o => o.TotalValue),
            buyers = Rank(activePos, o => o.IssuedByLabel, o => o.TotalValue),
            requesters = Rank(prs, r => r.RequesterLabel, r => r.TotalEstimatedValue),
            families = Rank(prItemValues, x => x.Family, x => x.Value),
            categories = Rank(poItemValues, x => CategoryOf(x.Family), x => x.Value),
            costCenters = Rank(prs, r => r.CostCenter, r => r.TotalEstimatedValue),
            regions = Rank(prs, r => RegionOf(r.CostCenter), r => r.TotalEstimatedValue),
            managers = Rank(prs, r => ManagerOf(r.CostCenter), r => r.TotalEstimatedValue),
            clients = Rank(prs, r => ClientOf(r.CostCenter), r => r.TotalEstimatedValue),
        };

        // ---- tabela fornecedor --------------------------------------------------
        var supplierTable = activePos.GroupBy(o => o.SupplierName)
            .Select(g =>
            {
                // OTIF: só pedidos com entrega encerrada e data prometida registrada contam
                var medidos = g.Where(o => o.Otif is not null).ToList();
                return new
                {
                    supplier = g.Key,
                    orders = g.Count(),
                    quantity = g.Sum(o => o.Items.Sum(i => i.Quantity)),
                    value = g.Sum(o => o.TotalValue),
                    open = g.Count(o => o.Status == PurchaseOrderStatus.Issued),
                    otifMeasured = medidos.Count,
                    otifPercent = medidos.Count > 0
                        ? Math.Round(medidos.Count(o => o.Otif == true) * 100.0 / medidos.Count, 1)
                        : (double?)null,
                };
            })
            .OrderByDescending(x => x.value).Take(20).ToList();

        // ---- prazos do processo: meta da família × realizado (slide 4) ----------
        var famList = await db.ProductFamilies.Where(f => f.Active).ToListAsync(ct);
        var prIds = prs.Select(r => r.Id).ToList();
        var quotes = await db.Quotations
            .Where(q => prIds.Contains(q.SourcePrId)
                        || q.Items.Any(i => i.SourcePrId != null && prIds.Contains(i.SourcePrId.Value)))
            .Select(q => new { q.Id, q.SourcePrId, q.CreatedAt, q.DirectorApprovedAt, q.PurchaseOrderId,
                               q.SavingValue, q.BaselineValue, q.NegotiatedValue, q.NegotiatedByLabel, q.Number,
                               ItemPrIds = q.Items.Where(i => i.SourcePrId != null)
                                   .Select(i => i.SourcePrId!.Value).Distinct().ToList() })
            .ToListAsync(ct);
        var poIds = quotes.Where(q => q.PurchaseOrderId != null).Select(q => q.PurchaseOrderId!.Value).ToList();
        var poDates = (await db.PurchaseOrders.Where(o => poIds.Contains(o.Id))
                .Select(o => new { o.Id, o.CreatedAt, o.DeliveryCompletedAt, o.SupplierName }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x);

        // cada solicitação entrega seus tempos às famílias dos itens dela
        var etapas = new Dictionary<string, List<double>[]>(StringComparer.OrdinalIgnoreCase);
        void Registrar(string familia, int etapa, double dias)
        {
            if (!etapas.TryGetValue(familia, out var listas))
                etapas[familia] = listas = [new(), new(), new(), new()];
            if (dias >= 0) listas[etapa].Add(dias);
        }
        foreach (var pr in prs)
        {
            var familias = pr.Items.Select(i => FamilyOf(i.CatalogItemId)).Where(f => f is not null)
                .Select(f => f!).Distinct().ToList();
            if (familias.Count == 0) familias = ["SEM FAMÍLIA"];
            var q = quotes.FirstOrDefault(x => x.SourcePrId == pr.Id || x.ItemPrIds.Contains(pr.Id));
            var po = q?.PurchaseOrderId is not null && poDates.TryGetValue(q.PurchaseOrderId.Value, out var o) ? o : null;
            foreach (var f in familias)
            {
                if (pr.SubmittedAt is not null && q is not null)
                    Registrar(f, 0, (q.CreatedAt - pr.SubmittedAt.Value).TotalDays);
                if (q?.DirectorApprovedAt is not null)
                    Registrar(f, 1, (q.DirectorApprovedAt.Value - q.CreatedAt).TotalDays);
                if (q?.DirectorApprovedAt is not null && po is not null)
                    Registrar(f, 2, (po.CreatedAt - q.DirectorApprovedAt.Value).TotalDays);
                if (po?.DeliveryCompletedAt is not null)
                    Registrar(f, 3, (po.DeliveryCompletedAt.Value - po.CreatedAt).TotalDays);
            }
        }
        static double? Media(List<double> v) => v.Count > 0 ? Math.Round(v.Average(), 1) : null;
        // SLA por processo (V2-P2): mediana e % dentro do prazo, medidos processo a processo
        static double? Mediana(List<double> v)
        {
            if (v.Count == 0) return null;
            var ord = v.OrderBy(x => x).ToList();
            var meio = ord.Count / 2;
            return Math.Round(ord.Count % 2 == 1 ? ord[meio] : (ord[meio - 1] + ord[meio]) / 2, 1);
        }
        static double? DentroPct(List<double> v, int? meta) =>
            meta is null || v.Count == 0 ? null : Math.Round(v.Count(d => d <= meta.Value) * 100.0 / v.Count, 1);

        var leadTimes = famList
            .Where(f => family is null || string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase))
            .Select(f =>
            {
                etapas.TryGetValue(f.Name, out var m);
                var listas = m ?? [new(), new(), new(), new()];
                var reais = listas.Select(Media).ToArray();
                var metas = new int?[] { f.LeadRequestToQuote, f.LeadQuoteToApproval, f.LeadApprovalToPo, f.LeadPoToDelivery };
                return new
                {
                    family = f.Name,
                    stages = new[] { "Solicitação → cotação", "Cotação → aprovação", "Aprovação → O.C.", "O.C. → entrega" }
                        .Select((rotulo, i) => new
                        {
                            stage = rotulo, target = metas[i], actual = reais[i],
                            median = Mediana(listas[i]),
                            measured = listas[i].Count,
                            withinSlaPct = DentroPct(listas[i], metas[i]),
                            late = metas[i] is not null && reais[i] is not null && reais[i] > metas[i],
                        }).ToList(),
                    targetTotal = f.LeadTotal,
                    actualTotal = reais.Any(v => v is not null)
                        ? Math.Round(reais.Where(v => v is not null).Sum(v => v!.Value), 1) : (double?)null,
                };
            })
            .Where(x => x.targetTotal is not null || x.stages.Any(e => e.actual is not null))
            .OrderBy(x => x.family).ToList();

        // ---- ganho de negociação (saving) ---------------------------------------
        var comSaving = quotes.Where(q => q.SavingValue > 0).ToList();
        // saving de referência (V2-P2): congelado nos itens das O.C.s do período
        var referenceSaving = activePos.Sum(o => o.Items.Sum(i => i.ReferenceSaving ?? 0));
        var referenceMeasured = activePos.Count(o => o.Items.Any(i => i.ReferenceSaving != null));
        var saving = new
        {
            total = comSaving.Sum(q => q.SavingValue ?? 0),
            baseline = comSaving.Sum(q => q.BaselineValue ?? 0),
            closed = comSaving.Sum(q => q.NegotiatedValue ?? 0),
            processes = comSaving.Count,
            referenceTotal = referenceSaving, referenceOrders = referenceMeasured,
            percent = comSaving.Sum(q => q.BaselineValue ?? 0) > 0
                ? Math.Round(comSaving.Sum(q => q.SavingValue ?? 0) / comSaving.Sum(q => q.BaselineValue ?? 0) * 100m, 1)
                : 0m,
            items = comSaving.OrderByDescending(q => q.SavingValue).Take(10).Select(q => new
            {
                number = q.Number,
                supplier = q.PurchaseOrderId is not null && poDates.TryGetValue(q.PurchaseOrderId.Value, out var o)
                    ? o.SupplierName : null,
                baseline = q.BaselineValue, closed = q.NegotiatedValue,
                value = q.SavingValue, byLabel = q.NegotiatedByLabel,
            }).ToList(),
        };

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

        return new { from, to, kpis, months, rankings, supplierTable, leadTimes, saving, filterOptions };
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

    /// <summary>
    /// Scorecard de fornecedores (V2-P3 §14): classes A/B/C/D na janela, derivadas de
    /// OTIF (peso 50), qualidade = 1 − devolvido/entregue (peso 30) e competitividade =
    /// vitórias/participações em cotações (peso 20). Componentes sem medição saem da conta
    /// (os pesos são renormalizados) — nada é persistido, nada bloqueia.
    /// </summary>
    public async Task<object> SupplierScorecardAsync(int monthsBack, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var from = now.AddMonths(-Math.Clamp(monthsBack, 1, 36));

        var pos = await db.PurchaseOrders.Include(o => o.Items)
            .Where(o => o.CreatedAt >= from && o.Status != PurchaseOrderStatus.Cancelled)
            .OrderByDescending(o => o.CreatedAt).Take(Cap).ToListAsync(ct);
        var proposals = await db.Proposals
            .Where(p => p.SubmittedAt >= from)
            .Select(p => new { p.SupplierId, p.QuotationId }).ToListAsync(ct);
        var wins = await db.Quotations
            .Where(q => q.SelectedAt >= from && q.WinnerSupplierId != null
                        && q.Status != QuotationStatus.Cancelled && q.Status != QuotationStatus.Rejected)
            .Select(q => new { SupplierId = q.WinnerSupplierId!.Value, q.Id }).ToListAsync(ct);
        var suppliers = await db.Suppliers.ToDictionaryAsync(s => s.Id, ct);

        var ids = pos.Select(o => o.SupplierId)
            .Concat(proposals.Select(p => p.SupplierId)).Distinct().ToList();
        var rows = new List<(double? score, object view)>();
        foreach (var sid in ids)
        {
            suppliers.TryGetValue(sid, out var sup);
            var minhasPos = pos.Where(o => o.SupplierId == sid).ToList();
            var medidas = minhasPos.Where(o => o.Otif is not null).ToList();
            double? otif = medidas.Count > 0
                ? medidas.Count(o => o.Otif == true) * 100.0 / medidas.Count : null;

            var entregue = minhasPos.SelectMany(o => o.Items).Sum(i => i.ReceivedQuantity + i.RejectedQuantity);
            var devolvido = minhasPos.SelectMany(o => o.Items).Sum(i => i.RejectedQuantity);
            double? qualidade = entregue > 0
                ? (double)((entregue - devolvido) * 100 / entregue) : null;

            var participacoes = proposals.Where(p => p.SupplierId == sid)
                .Select(p => p.QuotationId).Distinct().Count();
            var vitorias = wins.Count(w => w.SupplierId == sid);
            double? competitividade = participacoes > 0
                ? Math.Min(100.0, vitorias * 100.0 / participacoes) : null;

            // média ponderada só dos componentes medidos (pesos renormalizados)
            var partes = new List<(double valor, double peso)>();
            if (otif is not null) partes.Add((otif.Value, 0.5));
            if (qualidade is not null) partes.Add((qualidade.Value, 0.3));
            if (competitividade is not null) partes.Add((competitividade.Value, 0.2));
            double? score = partes.Count > 0
                ? Math.Round(partes.Sum(p => p.valor * p.peso) / partes.Sum(p => p.peso), 1) : null;
            var classe = score is null ? null
                : score >= 90 ? "A" : score >= 75 ? "B" : score >= 60 ? "C" : "D";

            rows.Add((score, new
            {
                supplierId = sid,
                supplierName = sup?.TradeName ?? sup?.LegalName ?? "—",
                homologation = sup?.HomologationStatus,
                score, grade = classe,
                otifPercent = otif is null ? (double?)null : Math.Round(otif.Value, 1),
                otifMeasured = medidas.Count,
                qualityPercent = qualidade is null ? (double?)null : Math.Round(qualidade.Value, 1),
                deliveredQuantity = entregue, rejectedQuantity = devolvido,
                winRatePercent = competitividade is null ? (double?)null : Math.Round(competitividade.Value, 1),
                proposals = participacoes, wins = vitorias,
                orders = minhasPos.Count,
                totalValue = minhasPos.Sum(o => o.TotalValue),
            }));
        }
        return new
        {
            months = Math.Clamp(monthsBack, 1, 36),
            items = rows.OrderByDescending(r => r.score ?? -1).Select(r => r.view).ToList(),
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
