using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Compliance;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Api.Insights;

/// <summary>
/// Um achado determinístico: regra fixa sobre dados reais, sempre com a
/// evidência — e, desde o INTEL-A, com o que fazer e onde.
/// </summary>
/// <param name="Action">A providência, em uma frase no imperativo.</param>
/// <param name="View">
/// Id da tela onde se age, no mesmo vocabulário dos avisos (`quotations`,
/// `suppliers`…). A interface resolve para a rota; achado sem destino usa null.
/// </param>
public record Insight(
    string Code, string Kind, string Severity, string Title, string Evidence,
    string Action, string? View = null);

/// <summary>
/// Procurement Insights (V2-P3): achados 100% determinísticos — nenhuma previsão, nenhum
/// modelo — sempre com a evidência que os sustenta:
///  INS-01 sobrepreço (item pago acima do último preço), INS-02 fracionamento (SCs pequenas
///  do mesmo centro somando acima do limite de alçada), INS-03 emergenciais recorrentes,
///  INS-04 concentração de fornecedor na categoria, INS-05 compras fechadas sem O.C. do ERP,
///  INS-06 fornecedor que atrasa de novo, INS-07 decisão com proposta única.
///
/// Cada achado carrega a evidência, a providência e a tela onde se age: descrever
/// um problema sem dizer o que fazer devolve o trabalho para quem lê (INTEL-A).
/// Mede e expõe — nunca bloqueia.
/// </summary>
public class InsightsService(AppDbContext db, ComplianceService compliance, TimeProvider clock)
{
    public static bool CanView(string role) =>
        role is Roles.Auditor or Roles.SupplyManager or Roles.Director or Roles.SystemAdministrator;

    private const decimal SobreprecoPct = 20m;      // % acima do último preço pago
    private const int FracionamentoDias = 30;       // janela em que SCs pequenas se somam
    private const int EmergenciaisMinimo = 3;       // urgências no período que viram padrão
    private const double ConcentracaoPct = 40;      // % do spend da categoria em um fornecedor
    private const int SemOcMinimo = 3;              // fechamentos sem O.C. do ERP que viram padrão
    private const int AtrasosMinimo = 3;            // atrasos do mesmo fornecedor que viram padrão

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
                    $"O.C. {o.Number} ({o.SupplierName}): pago {i.UnitPrice:0.00} contra último preço {i.LastPaidUnitPrice:0.00}.",
                    $"Confira o pedido {o.Number} e leve o preço anterior para a próxima negociação com {o.SupplierName}.",
                    "buy-orders"));
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
                            $"(limite de alçada {cc.Limite:0.00}): {string.Join(", ", grupo.Select(g => g.Number))}.",
                            "Abra essas SCs numa cotação só: junto elas passam do limite do centro e " +
                            "precisam da alçada correspondente — e ainda dão escala para negociar.",
                            "triage"));
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
                $"{string.Join(", ", g.Take(6).Select(r => r.Number))}{(g.Count() > 6 ? "…" : "")}.",
                $"Converse com o responsável pelo centro {g.Key}: urgência repetida costuma ser " +
                "compra que dava para prever, e ela custa mais caro.",
                "triage"));

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
                    $"{top.Fornecedor} responde por {share}% do spend da categoria ({top.Valor:0.00} de {total:0.00}).",
                    $"Convide outros fornecedores homologados para as próximas cotações de {cat.Key} — " +
                    "depender de um só encarece e deixa a operação exposta se ele falhar.",
                    "suppliers"));
        }

        // INS-05 — compra fechada sem O.C. do ERP (PO-BR-011). A observação é a
        // exceção prevista e mantém o fechamento honesto; o que não pode é a
        // exceção virar rotina, e é isso que este achado mede.
        var semOc = pos.Where(o => string.IsNullOrWhiteSpace(o.ErpNumber)
                                   && !string.IsNullOrWhiteSpace(o.NoErpReason)).ToList();
        if (semOc.Count >= SemOcMinimo)
            insights.Add(new("INS-05", "SEM_OC", semOc.Count >= SemOcMinimo * 2 ? "alta" : "media",
                $"{semOc.Count} compras fechadas sem O.C. do ERP",
                $"Fecharam com a justificativa da exceção, e não com número do SENIOR: " +
                $"{string.Join(", ", semOc.Take(6).Select(o => o.Number))}{(semOc.Count > 6 ? "…" : "")}.",
                "Confira com o time por que a O.C. não sai do ERP nesses casos. A exceção existe "
                + "para o imprevisto; virando rotina, o relatório de O.C. deixa de descrever a operação.",
                "buy-orders"));

        // INS-06 — fornecedor que atrasa de novo. O OTIF já é medido por pedido;
        // aqui ele vira padrão de comportamento, que é o que muda uma decisão.
        var atrasos = pos.Where(o => o.OnTime == false).GroupBy(o => o.SupplierName)
            .Where(g => g.Count() >= AtrasosMinimo).ToList();
        foreach (var g in atrasos)
        {
            var entregues = pos.Count(o => o.SupplierName == g.Key && o.OnTime != null);
            var pct = entregues > 0 ? Math.Round(g.Count() * 100m / entregues, 1) : 0;
            insights.Add(new("INS-06", "ATRASO_FORNECEDOR", pct >= 50 ? "alta" : "media",
                $"{g.Key} entregou fora do prazo {g.Count()} vezes",
                $"{g.Count()} de {entregues} entregas medidas ({pct}%) passaram da data prometida: " +
                $"{string.Join(", ", g.Take(6).Select(o => o.Number))}{(g.Count() > 6 ? "…" : "")}.",
                $"Leve o histórico para a próxima negociação com {g.Key} e considere o prazo real, "
                + "e não o prometido, ao comparar as propostas.",
                "scorecard"));
        }

        // INS-07 — decisão com proposta única: não é irregular, mas comprar sem
        // comparação é o caso em que o preço não tem contra o que ser medido.
        var decididas = await db.Quotations.Include(q => q.Proposals)
            .Where(q => q.CreatedAt >= from && q.WinnerProposalId != null)
            .Select(q => new { q.Number, q.CostCenter, Propostas = q.Proposals.Count })
            .ToListAsync(ct);
        var unicas = decididas.Where(q => q.Propostas <= 1).ToList();
        if (unicas.Count > 0)
            insights.Add(new("INS-07", "PROPOSTA_UNICA", "media",
                $"{unicas.Count} processo(s) decidido(s) com uma proposta só",
                $"Sem concorrência não há com o que comparar o preço: " +
                $"{string.Join(", ", unicas.Take(6).Select(q => q.Number))}{(unicas.Count > 6 ? "…" : "")}.",
                "Convide ao menos mais um fornecedor homologado nas próximas cotações desses centros.",
                "quotations"));

        var ordem = new Dictionary<string, int> { ["alta"] = 0, ["media"] = 1, ["info"] = 2 };
        return insights.OrderBy(i => ordem.GetValueOrDefault(i.Severity, 3)).ThenBy(i => i.Code)
            .Take(100).ToList();
    }

    /// <summary>
    /// O relatório do painel, com os achados <b>também</b> em lista tipada: o gatilho lê a
    /// mesma lista que a tela mostra, e não a recalcula. Dois cálculos dariam dois conjuntos
    /// de achados na mesma requisição, e o plano poderia nascer de um que a tela não mostrou.
    /// </summary>
    public async Task<(object Relatorio, IReadOnlyList<Insight> Achados)> ReportAsync(
        int monthsBack, CancellationToken ct = default)
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
            // cost avoidance (V2-P4): reajustes pleiteados × fechados, congelados no registro
            costAvoidanceTotal = await db.ContractAdjustments
                .Where(a => a.CreatedAt >= from).SumAsync(a => a.CostAvoidance, ct),
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

        return (new
        {
            months = Math.Clamp(monthsBack, 1, 36),
            executive, backlog,
            insights = insights
                .Select(i => new
                {
                    code = i.Code, kind = i.Kind, severity = i.Severity,
                    title = i.Title, evidence = i.Evidence, action = i.Action, view = i.View,
                })
                .ToList(),
        }, insights);
    }
}
