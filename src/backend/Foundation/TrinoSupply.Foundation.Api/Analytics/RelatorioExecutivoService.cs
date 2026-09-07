using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Api.Analytics;

/// <summary>Recorte do relatório: período mais os três cortes que a diretoria pede.</summary>
public record FiltroRelatorio(DateOnly From, DateOnly To, string? Company, string? CostCenter, Guid? BuyerId);

public record LinhaFamilia(string Family, decimal Value, decimal Quantity, int Orders, double Percent);
public record LinhaSaving(string Buyer, int Processes, decimal Baseline, decimal Closed, decimal Saving,
    double SavingPercent, decimal Spend, int Orders);
public record LinhaFornecedor(string Supplier, int Orders, decimal Value, double Percent);
public record LinhaOtif(string Supplier, int Measured, double? OnTimePercent, double? InFullPercent, double? OtifPercent);
public record CompraUrgente(string Number, string? PrNumber, string Supplier, decimal Value,
    DateOnly IssuedOn, string? Requester, string? Reason, string? Impact);
public record CompraSemOc(string Number, string Supplier, decimal Value, DateOnly IssuedOn,
    string Buyer, string? CostCenter, string? Reason);

public record BlocoFornecedores(IReadOnlyList<LinhaFornecedor> Rows, int SupplierCount,
    double? Top1Percent, double? Top3Percent, double? Top5Percent);
public record BlocoUrgencia(int Orders, decimal Value, double Percent, IReadOnlyList<CompraUrgente> Items);
/// <summary>
/// Compras sem O.C. do ERP. <c>Orders</c>/<c>Value</c> são as que **fecharam pela
/// exceção** do PO-BR-011, com a justificativa — o que a diretoria pediu para ver.
/// <c>PendingOrders</c> são as que ainda vão registrar a O.C. (fila, não violação) e
/// <c>ClosedWithoutReason</c> são a anomalia: andaram sem O.C. e sem justificativa.
/// </summary>
public record BlocoSemOc(int Orders, decimal Value, double Percent,
    int PendingOrders, decimal PendingValue, int ClosedWithoutReason, IReadOnlyList<CompraSemOc> Items);

public record OpcoesDeFiltro(IReadOnlyList<string> Companies,
    IReadOnlyList<OpcaoCentroCusto> CostCenters, IReadOnlyList<OpcaoComprador> Buyers);
public record OpcaoCentroCusto(string Code, string Name);
public record OpcaoComprador(Guid Id, string Label);

public record KpisDoRelatorio(decimal Spend, int Orders, int Suppliers, decimal SavingTotal,
    double? SavingPercent, double UrgentPercent, double? OtifPercent, decimal WithoutErpValue);

/// <summary>
/// Quanto do recorte o relatório de fato cobre. Existe porque o pedido de compra
/// **não** carrega empresa nem centro de custo: os dois vêm da solicitação de origem
/// (<c>SourcePrId</c>). Pedido criado direto, sem SC, não entra em nenhum corte por
/// empresa ou centro de custo — e calar isso faria a diretoria somar um total que
/// não fecha com o spend do período.
/// </summary>
public record CoberturaDoRelatorio(int OrdersWithoutPr, decimal ValueWithoutPr, bool Capped, int Cap);

public record RelatorioExecutivo(
    DateOnly From, DateOnly To, FiltroRelatorio Filters, string? CompanyLabel, string? CostCenterLabel,
    string? BuyerLabel, DateTimeOffset GeneratedAt,
    KpisDoRelatorio Kpis, CoberturaDoRelatorio Coverage,
    IReadOnlyList<LinhaFamilia> Families, IReadOnlyList<LinhaSaving> Buyers,
    BlocoFornecedores Suppliers, BlocoUrgencia Urgent, IReadOnlyList<LinhaOtif> Otif,
    BlocoSemOc WithoutErp, OpcoesDeFiltro FilterOptions);

/// <summary>
/// Relatório executivo de compras: os seis blocos que a diretoria pede sobre um mesmo
/// recorte — o que foi comprado por família, o saving que cada comprador negociou, a
/// concentração por fornecedor, o peso das compras urgentes, a entrega no prazo (OTIF)
/// e as compras que fecharam sem O.C. do ERP, com a justificativa (PO-BR-011).
///
/// Somente leitura. O universo é o **pedido de compra** do período (o dinheiro que saiu),
/// nunca a solicitação — solicitação é intenção, e um relatório de diretoria que soma
/// intenção com compra não fecha com o financeiro.
/// </summary>
public class RelatorioExecutivoService(AppDbContext db, TimeProvider clock)
{
    /// <summary>Mesma janela analítica do resto dos dashboards; passar disso o relatório avisa.</summary>
    public const int Cap = 5000;

    /// <summary>Quem vê o dashboard de suprimentos vê o relatório: é o mesmo dado, consolidado.</summary>
    public static bool CanView(string role) => AnalyticsService.CanViewSupply(role);

    public async Task<RelatorioExecutivo> GerarAsync(FiltroRelatorio filtro, CancellationToken ct = default)
    {
        var fromDt = new DateTimeOffset(filtro.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toDt = new DateTimeOffset(filtro.To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // ---- universo: pedidos vivos do período ---------------------------------
        var todos = await db.PurchaseOrders.Include(o => o.Items)
            .Where(o => o.CreatedAt >= fromDt && o.CreatedAt < toDt && o.Status != PurchaseOrderStatus.Cancelled)
            .OrderByDescending(o => o.CreatedAt).Take(Cap).ToListAsync(ct);

        // ---- de onde vêm empresa e centro de custo -------------------------------
        // O pedido não guarda nenhum dos dois. O caminho é a SC de origem: dela sai o
        // centro de custo e, pelo cadastro do CC, a empresa (o mesmo caminho que o
        // cabeçalho do PDF da O.C. já usa). A empresa digitada na SC fica de reserva
        // para o CC que ainda não tem CNPJ vinculado.
        var prIds = todos.Where(o => o.SourcePrId is not null).Select(o => o.SourcePrId!.Value).Distinct().ToList();
        var prs = (await db.Requisitions.Where(r => prIds.Contains(r.Id))
                .Select(r => new
                {
                    r.Id, r.Number, r.CostCenter, r.Company, r.Priority,
                    r.UrgencyReason, r.UrgencyImpact, r.RequesterLabel,
                }).ToListAsync(ct))
            .ToDictionary(x => x.Id);

        var centros = await db.CostCenters.Select(c => new { c.Code, c.Name, c.CompanyId, c.Active }).ToListAsync(ct);
        var ccPorCodigo = centros.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
        var empresas = await db.Companies.Select(c => new { c.Id, c.LegalName, c.Active }).ToListAsync(ct);
        var empresaPorId = empresas.ToDictionary(c => c.Id, c => c.LegalName);

        string? CentroDe(PurchaseOrder o) =>
            o.SourcePrId is not null && prs.TryGetValue(o.SourcePrId.Value, out var pr)
                ? (string.IsNullOrWhiteSpace(pr.CostCenter) ? null : pr.CostCenter) : null;

        string? EmpresaDe(PurchaseOrder o)
        {
            var codigo = CentroDe(o);
            if (codigo is not null && ccPorCodigo.TryGetValue(codigo, out var cc)
                && cc.CompanyId is { } id && empresaPorId.TryGetValue(id, out var nome))
                return nome;
            return o.SourcePrId is not null && prs.TryGetValue(o.SourcePrId.Value, out var pr)
                   && !string.IsNullOrWhiteSpace(pr.Company) ? pr.Company : null;
        }

        // ---- os cortes ----------------------------------------------------------
        var pos = todos.Where(o =>
                (filtro.BuyerId is null || o.IssuedBy == filtro.BuyerId) &&
                (filtro.CostCenter is null
                 || string.Equals(CentroDe(o), filtro.CostCenter, StringComparison.OrdinalIgnoreCase)) &&
                (filtro.Company is null
                 || string.Equals(EmpresaDe(o), filtro.Company, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var spend = pos.Sum(o => o.TotalValue);
        decimal Fatia(decimal parte) => spend > 0 ? parte * 100 / spend : 0;
        double Pct(decimal parte) => Math.Round((double)Fatia(parte), 1);

        // ---- 1. o que foi comprado, por família ---------------------------------
        // A família viaja no próprio item da O.C. (compra dividida grava ali qual lote
        // o fornecedor ganhou). Sem ela, o produto de catálogo responde.
        var familiaPorItem = (await db.CatalogItems.Select(i => new { i.Id, i.Family }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Family);
        string FamiliaDe(PurchaseOrderItem i) =>
            !string.IsNullOrWhiteSpace(i.Family) ? i.Family!
            : i.CatalogItemId is { } id && familiaPorItem.TryGetValue(id, out var f) && !string.IsNullOrWhiteSpace(f)
                ? f : "SEM FAMÍLIA";

        var families = pos.SelectMany(o => o.Items.Select(i => new
            {
                Familia = FamiliaDe(i), Valor = (i.UnitPrice ?? 0) * i.Quantity, i.Quantity, Pedido = o.Id,
            }))
            .GroupBy(x => x.Familia)
            .Select(g => new LinhaFamilia(g.Key, g.Sum(x => x.Valor), g.Sum(x => x.Quantity),
                g.Select(x => x.Pedido).Distinct().Count(), Pct(g.Sum(x => x.Valor))))
            .OrderByDescending(x => x.Value).ToList();

        // ---- 2. saving por comprador --------------------------------------------
        // O saving mora no processo de cotação, não no pedido — e um processo dividido
        // por família gera mais de um pedido. Cada cotação só entra **uma vez**, senão a
        // compra dividida infla o ganho em duas ou três vezes.
        var cotacaoIds = pos.Where(o => o.QuotationId is not null).Select(o => o.QuotationId!.Value).Distinct().ToList();
        var cotacoes = (await db.Quotations.Where(q => cotacaoIds.Contains(q.Id))
                .Select(q => new { q.Id, q.BaselineValue, q.NegotiatedValue, q.SavingValue }).ToListAsync(ct))
            .ToDictionary(x => x.Id);

        var jaContadas = new HashSet<Guid>();
        var porComprador = new Dictionary<string, (int processos, decimal baseline, decimal fechado, decimal saving, decimal spend, int pedidos)>();
        foreach (var o in pos.OrderBy(o => o.CreatedAt))
        {
            var chave = string.IsNullOrWhiteSpace(o.IssuedByLabel) ? "—" : o.IssuedByLabel;
            var atual = porComprador.GetValueOrDefault(chave);
            atual.spend += o.TotalValue;
            atual.pedidos += 1;
            if (o.QuotationId is { } qid && jaContadas.Add(qid) && cotacoes.TryGetValue(qid, out var q))
            {
                atual.processos += 1;
                atual.baseline += q.BaselineValue ?? 0;
                atual.fechado += q.NegotiatedValue ?? 0;
                atual.saving += q.SavingValue ?? 0;
            }
            porComprador[chave] = atual;
        }
        var buyers = porComprador
            .Select(kv => new LinhaSaving(kv.Key, kv.Value.processos, kv.Value.baseline, kv.Value.fechado,
                kv.Value.saving,
                kv.Value.baseline > 0 ? Math.Round((double)(kv.Value.saving * 100 / kv.Value.baseline), 1) : 0,
                kv.Value.spend, kv.Value.pedidos))
            .OrderByDescending(b => b.Saving).ThenByDescending(b => b.Spend).ToList();

        // ---- 3. concentração por fornecedor -------------------------------------
        var fornecedores = pos.GroupBy(o => string.IsNullOrWhiteSpace(o.SupplierName) ? "—" : o.SupplierName)
            .Select(g => new LinhaFornecedor(g.Key, g.Count(), g.Sum(o => o.TotalValue), Pct(g.Sum(o => o.TotalValue))))
            .OrderByDescending(f => f.Value).ToList();
        double? Acumulado(int n) => fornecedores.Count == 0 ? null
            : Math.Round((double)Fatia(fornecedores.Take(n).Sum(f => f.Value)), 1);
        var suppliers = new BlocoFornecedores(fornecedores, fornecedores.Count,
            Acumulado(1), Acumulado(3), Acumulado(5));

        // ---- 4. peso das compras urgentes ---------------------------------------
        // Urgência é atributo da SC (PR-ERR-050 obriga o motivo e o impacto). Pedido sem
        // SC de origem não é urgente nem deixa de ser: fica de fora, e a cobertura diz quantos são.
        var urgentes = pos.Where(o => o.SourcePrId is not null
                                      && prs.TryGetValue(o.SourcePrId.Value, out var pr)
                                      && pr.Priority == "URGENT").ToList();
        var urgentValue = urgentes.Sum(o => o.TotalValue);
        var urgent = new BlocoUrgencia(urgentes.Count, urgentValue, Pct(urgentValue),
            urgentes.OrderByDescending(o => o.TotalValue).Take(25).Select(o =>
            {
                var pr = prs[o.SourcePrId!.Value];
                return new CompraUrgente(o.Number, pr.Number, o.SupplierName, o.TotalValue,
                    DateOnly.FromDateTime(o.CreatedAt.UtcDateTime), pr.RequesterLabel,
                    pr.UrgencyReason, pr.UrgencyImpact);
            }).ToList());

        // ---- 5. entrega no prazo (OTIF) por fornecedor --------------------------
        // Só entra pedido com entrega encerrada e data prometida registrada — é o que
        // `Otif` já exige. O resto seria opinião sobre entrega que ainda não terminou.
        var otif = pos.Where(o => o.Otif is not null)
            .GroupBy(o => string.IsNullOrWhiteSpace(o.SupplierName) ? "—" : o.SupplierName)
            .Select(g => new LinhaOtif(g.Key, g.Count(),
                Math.Round(g.Count(o => o.OnTime == true) * 100.0 / g.Count(), 1),
                Math.Round(g.Count(o => o.InFull == true) * 100.0 / g.Count(), 1),
                Math.Round(g.Count(o => o.Otif == true) * 100.0 / g.Count(), 1)))
            .OrderBy(x => x.OtifPercent).ThenByDescending(x => x.Measured).ToList();
        var medidos = pos.Count(o => o.Otif is not null);
        double? otifGeral = medidos > 0
            ? Math.Round(pos.Count(o => o.Otif == true) * 100.0 / medidos, 1) : null;

        // ---- 6. compras sem O.C. do ERP, com a justificativa --------------------
        // PO-BR-011: sem O.C. no SENIOR a compra não fecha, salvo a observação dizendo
        // por quê. Duas situações diferentes moram embaixo de "sem `erp_number`", e
        // juntá-las faria a diretoria ler violação onde só há fila:
        //
        // | Situação | O que é | Como entra |
        // |---|---|---|
        // | tem `no_erp_reason` | fechou pela exceção — auditável | é **o** bloco |
        // | não tem nem um nem outro | O.C. ainda por registrar | conta à parte |
        //
        // A separação não é opinião: `AddInvoiceAsync` recusa faturar sem um dos dois
        // (PO-ERR-052), então pedido sem os dois é pedido que ainda não andou. O que
        // sobra — faturado ou recebido sem O.C. e sem justificativa — é anomalia de
        // dado anterior à trava, e sai destacado em vez de somado ao resto.
        var semOc = pos.Where(o => string.IsNullOrWhiteSpace(o.ErpNumber)).ToList();
        var pelaExcecao = semOc.Where(o => !string.IsNullOrWhiteSpace(o.NoErpReason)).ToList();
        var semNadaFechadas = semOc.Where(o => string.IsNullOrWhiteSpace(o.NoErpReason)
                                               && o.Status != PurchaseOrderStatus.Issued).ToList();
        var aRegistrar = semOc.Where(o => string.IsNullOrWhiteSpace(o.NoErpReason)
                                          && o.Status == PurchaseOrderStatus.Issued).ToList();
        var semOcValor = pelaExcecao.Sum(o => o.TotalValue);
        CompraSemOc Linha(PurchaseOrder o) => new(o.Number, o.SupplierName, o.TotalValue,
            DateOnly.FromDateTime(o.CreatedAt.UtcDateTime), o.IssuedByLabel, CentroDe(o), o.NoErpReason);
        var withoutErp = new BlocoSemOc(pelaExcecao.Count, semOcValor, Pct(semOcValor),
            aRegistrar.Count, aRegistrar.Sum(o => o.TotalValue), semNadaFechadas.Count,
            pelaExcecao.Concat(semNadaFechadas).OrderByDescending(o => o.TotalValue)
                .Take(50).Select(Linha).ToList());

        // ---- KPIs, cobertura e opções de filtro ---------------------------------
        var savingTotal = buyers.Sum(b => b.Saving);
        var baselineTotal = buyers.Sum(b => b.Baseline);
        var kpis = new KpisDoRelatorio(spend, pos.Count,
            pos.Select(o => o.SupplierId).Distinct().Count(), savingTotal,
            baselineTotal > 0 ? Math.Round((double)(savingTotal * 100 / baselineTotal), 1) : null,
            Pct(urgentValue), otifGeral, semOcValor);

        var semSc = pos.Where(o => o.SourcePrId is null || !prs.ContainsKey(o.SourcePrId.Value)).ToList();
        var coverage = new CoberturaDoRelatorio(semSc.Count, semSc.Sum(o => o.TotalValue),
            todos.Count >= Cap, Cap);

        var empresasDaSc = await db.Requisitions.Where(r => r.Company != null && r.Company != "")
            .Select(r => r.Company!).Distinct().Take(200).ToListAsync(ct);
        var filterOptions = new OpcoesDeFiltro(
            empresas.Where(c => c.Active).Select(c => c.LegalName).Concat(empresasDaSc)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
            centros.Where(c => c.Active).OrderBy(c => c.Code)
                .Select(c => new OpcaoCentroCusto(c.Code, c.Name)).ToList(),
            (await db.PurchaseOrders.Select(o => new { o.IssuedBy, o.IssuedByLabel })
                    .Distinct().OrderBy(x => x.IssuedByLabel).Take(200).ToListAsync(ct))
                .Select(x => new OpcaoComprador(x.IssuedBy, x.IssuedByLabel)).ToList());

        // rótulos do recorte: o cabeçalho do PDF precisa dizer o que foi filtrado, e
        // "CC-ADM-001" sozinho não conta a mesma coisa que "CC-ADM-001 — Administrativo"
        var centroLabel = filtro.CostCenter is null ? null
            : ccPorCodigo.TryGetValue(filtro.CostCenter, out var alvo) ? $"{alvo.Code} — {alvo.Name}" : filtro.CostCenter;
        var compradorLabel = filtro.BuyerId is null ? null
            : pos.FirstOrDefault()?.IssuedByLabel
              ?? await db.PurchaseOrders.Where(o => o.IssuedBy == filtro.BuyerId)
                     .Select(o => o.IssuedByLabel).FirstOrDefaultAsync(ct);

        return new RelatorioExecutivo(filtro.From, filtro.To, filtro,
            filtro.Company, centroLabel, compradorLabel, clock.GetUtcNow(),
            kpis, coverage, families, buyers, suppliers, urgent, otif, withoutErp, filterOptions);
    }
}
