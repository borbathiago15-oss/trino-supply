using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Compliance;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Api.Insights;

/// <summary>Um achado determinístico: regra fixa sobre dados reais, sempre com a evidência.</summary>
public record Insight(string Code, string Kind, string Severity, string Title, string Evidence);

/// <summary>
/// Procurement Insights (V2-P3): achados 100% determinísticos — nenhuma previsão, nenhum
/// modelo — sempre com a evidência que os sustenta:
///  INS-01 sobrepreço (item pago acima do último preço), INS-02 fracionamento (SCs pequenas
///  do mesmo centro somando acima do limite de alçada), INS-03 emergenciais recorrentes,
///  INS-04 concentração de fornecedor na categoria. Mede e expõe — nunca bloqueia.
/// </summary>
public class InsightsService(AppDbContext db, ComplianceService compliance, TimeProvider clock)
{
    public static bool CanView(string role) =>
        role is Roles.Auditor or Roles.SupplyManager or Roles.Director or Roles.SystemAdministrator;

    private const decimal SobreprecoPct = 20m;      // % acima do último preço pago
    private const int FracionamentoDias = 30;       // janela em que SCs pequenas se somam
    private const int EmergenciaisMinimo = 3;       // urgências no período que viram padrão
    private const double ConcentracaoPct = 40;      // % do spend da categoria em um fornecedor

    /// <summary>Os achados do período, já ordenados por severidade (público para os testes).</summary>
    public async Task<List<Insight>> FindInsightsAsync(int monthsBack, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var from = now.AddMonths(-Math.Clamp(monthsBack, 1, 36));
        var insights = new List<Insight>();

        var pos = await db.PurchaseOrders.Include(o => o.Items)
            .Where(o => o.CreatedAt >= from && o.Status != PurchaseOrderStatus.Cancelled)
            .OrderByDescending(o => o.CreatedAt).Take(5000).ToListAsync(ct);

        // INS-01 — sobrepreço: preço fechado acima do último preço pago congelado na O.C.
        foreach (var o in pos)
            foreach (var i in o.Items.Where(i => i.LastPaidUnitPrice > 0 && i.UnitPrice is not null
                        && i.UnitPrice > i.LastPaidUnitPrice * (1 + SobreprecoPct / 100)))
            {
                var pct = Math.Round((i.UnitPrice!.Value / i.LastPaidUnitPrice!.Value - 1) * 100, 1);
                insights.Add(new("INS-01", "SOBREPRECO", pct >= 50 ? "alta" : "media",
                    $"Sobrepreço de {pct}% em {i.Description}",
                    $"O.C. {o.Number} ({o.SupplierName}): pago {i.UnitPrice:0.00} contra último preço {i.LastPaidUnitPrice:0.00}."));
            }

        // INS-02 — fracionamento: SCs individualmente abaixo do limite do centro que, somadas
        // numa janela curta, passam dele (o gatilho exige o limite por nível — item 20 do P3)
        var limites = await db.CostCenters
            .Where(c => c.Active && c.Level2ValueLimit != null)
            .Select(c => new { c.Code, Limite = c.Level2ValueLimit!.Value }).ToListAsync(ct);
        if (limites.Count > 0)
        {
            var prsJanela = await db.Requisitions
                .Where(r => r.DeletedAt == null && r.CreatedAt >= from
                            && r.Status != RequisitionStatus.Rejected && r.Status != RequisitionStatus.Cancelled)
                // TotalEstimatedValue é derivado dos itens: a soma precisa vir do banco
                .Select(r => new { r.Number, r.CostCenter, r.CreatedAt,
                    TotalEstimatedValue = r.Items.Sum(i => (i.EstimatedUnitPrice ?? 0) * i.Quantity) })
                .ToListAsync(ct);
            foreach (var cc in limites)
            {
                var pequenas = prsJanela
                    .Where(r => r.CostCenter.Equals(cc.Code, StringComparison.OrdinalIgnoreCase)
                                && r.TotalEstimatedValue > 0 && r.TotalEstimatedValue <= cc.Limite)
                    .OrderBy(r => r.CreatedAt).ToList();
                for (var i = 0; i < pequenas.Count; i++)
                {
                    var grupo = pequenas.Skip(i)
                        .TakeWhile(r => (r.CreatedAt - pequenas[i].CreatedAt).TotalDays <= FracionamentoDias)
                        .ToList();
                    if (grupo.Count >= 2 && grupo.Sum(r => r.TotalEstimatedValue) > cc.Limite)
                    {
                        insights.Add(new("INS-02", "FRACIONAMENTO", "alta",
                            $"Possível fracionamento no centro {cc.Code}",
                            $"{grupo.Count} SCs em {FracionamentoDias} dias somam {grupo.Sum(r => r.TotalEstimatedValue):0.00} " +
                            $"(limite de alçada {cc.Limite:0.00}): {string.Join(", ", grupo.Select(g => g.Number))}."));
                        break;   // um achado por centro basta para investigar
                    }
                }
            }
        }

        // INS-03 — emergenciais recorrentes por centro de custo
        var urgentes = await db.Requisitions
            .Where(r => r.DeletedAt == null && r.CreatedAt >= from && r.Priority == "URGENT")
            .Select(r => new { r.Number, r.CostCenter }).ToListAsync(ct);
        foreach (var g in urgentes.GroupBy(r => r.CostCenter.ToUpperInvariant())
                     .Where(g => g.Count() >= EmergenciaisMinimo))
            insights.Add(new("INS-03", "EMERGENCIAIS", "media",
                $"Urgências recorrentes no centro {g.Key}",
                $"{g.Count()} SCs urgentes no período — planejamento de demanda merece revisão: " +
                $"{string.Join(", ", g.Take(6).Select(r => r.Number))}{(g.Count() > 6 ? "…" : "")}."));

        // INS-04 — concentração de fornecedor na categoria (≥40% do spend)
        var familyById = (await db.CatalogItems.Select(i => new { i.Id, i.Family }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Family);
        var categoryByFamily = (await db.ProductFamilies
                .Select(f => new { f.Name, f.Category }).ToListAsync(ct))
            .ToDictionary(x => x.Name, x => x.Category, StringComparer.OrdinalIgnoreCase);
        string CategoriaDe(Guid? catalogItemId)
        {
            if (catalogItemId is null || !familyById.TryGetValue(catalogItemId.Value, out var fam)) return "SEM CATEGORIA";
            return categoryByFamily.TryGetValue(fam, out var c) && !string.IsNullOrWhiteSpace(c) ? c! : "SEM CATEGORIA";
        }
        var spend = pos.SelectMany(o => o.Items.Select(i => new
        {
            Categoria = CategoriaDe(i.CatalogItemId), o.SupplierName,
            Valor = (i.UnitPrice ?? 0) * i.Quantity,
        })).Where(x => x.Valor > 0 && x.Categoria != "SEM CATEGORIA").ToList();
        foreach (var cat in spend.GroupBy(x => x.Categoria))
        {
            var total = cat.Sum(x => x.Valor);
            if (total <= 0) continue;
            var top = cat.GroupBy(x => x.SupplierName)
                .Select(g => new { Fornecedor = g.Key, Valor = g.Sum(x => x.Valor) })
                .OrderByDescending(x => x.Valor).First();
            var share = Math.Round((double)(top.Valor * 100 / total), 1);
            if (share >= ConcentracaoPct && cat.Select(x => x.SupplierName).Distinct().Count() > 1)
                insights.Add(new("INS-04", "CONCENTRACAO", share >= 70 ? "alta" : "media",
                    $"Concentração na categoria {cat.Key}",
                    $"{top.Fornecedor} responde por {share}% do spend da categoria ({top.Valor:0.00} de {total:0.00})."));
        }

        var ordem = new Dictionary<string, int> { ["alta"] = 0, ["media"] = 1, ["info"] = 2 };
        return insights.OrderBy(i => ordem.GetValueOrDefault(i.Severity, 3)).ThenBy(i => i.Code)
            .Take(100).ToList();
    }

    public async Task<object> ReportAsync(int monthsBack, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var from = now.AddMonths(-Math.Clamp(monthsBack, 1, 36));
        var insights = await FindInsightsAsync(monthsBack, ct);
        var pos = await db.PurchaseOrders.Include(o => o.Items)
            .Where(o => o.CreatedAt >= from && o.Status != PurchaseOrderStatus.Cancelled)
            .OrderByDescending(o => o.CreatedAt).Take(5000).ToListAsync(ct);

        // ---- visão executiva: os números que a diretoria acompanha ----------------
        var quotes = await db.Quotations
            .Where(q => q.CreatedAt >= from && q.Status != QuotationStatus.Cancelled && q.Status != QuotationStatus.Rejected)
            .Select(q => new { q.SavingValue, q.Status }).ToListAsync(ct);
        var otifMedidas = pos.Where(o => o.Otif is not null).ToList();
        var cp = await compliance.ReportAsync(null, ct);
        var executive = new
        {
            spend = pos.Sum(o => o.TotalValue),
            orders = pos.Count,
            processes = quotes.Count,
            closedProcesses = quotes.Count(q => q.Status == QuotationStatus.PoIssued),
            savingTotal = quotes.Sum(q => q.SavingValue ?? 0),
            referenceSavingTotal = pos.Sum(o => o.Items.Sum(i => i.ReferenceSaving ?? 0)),
            otifPercent = otifMedidas.Count > 0
                ? Math.Round(otifMedidas.Count(o => o.Otif == true) * 100.0 / otifMedidas.Count, 1) : (double?)null,
            complianceAverage = cp.AverageScore,
        };

        // ---- backlog: o que está aberto agora, por faixa de espera e por responsável ----
        var abertas = await db.Requisitions
            .Where(r => r.DeletedAt == null
                        && (r.Status == RequisitionStatus.Submitted || r.Status == RequisitionStatus.Approved
                            || r.Status == RequisitionStatus.InApproval))
            .Select(r => new { r.SubmittedAt, r.CreatedAt, r.AssignedToLabel }).ToListAsync(ct);
        int DiasDe(DateTimeOffset? d) => (int)Math.Floor((now - (d ?? now)).TotalDays);
        var faixas = new[] { ("0–2 dias", 0, 2), ("3–5 dias", 3, 5), ("6–10 dias", 6, 10), ("+10 dias", 11, int.MaxValue) };
        var backlog = new
        {
            total = abertas.Count,
            unassigned = abertas.Count(a => a.AssignedToLabel is null),
            aging = faixas.Select(f => new
            {
                label = f.Item1,
                count = abertas.Count(a =>
                {
                    var dias = DiasDe(a.SubmittedAt ?? a.CreatedAt);
                    return dias >= f.Item2 && dias <= f.Item3;
                }),
            }).ToList(),
            byAssignee = abertas.Where(a => a.AssignedToLabel is not null)
                .GroupBy(a => a.AssignedToLabel!)
                .Select(g => new { label = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count).Take(10).ToList(),
        };

        return new
        {
            months = Math.Clamp(monthsBack, 1, 36),
            executive, backlog,
            insights = insights
                .Select(i => new { code = i.Code, kind = i.Kind, severity = i.Severity, title = i.Title, evidence = i.Evidence })
                .ToList(),
        };
    }
}
