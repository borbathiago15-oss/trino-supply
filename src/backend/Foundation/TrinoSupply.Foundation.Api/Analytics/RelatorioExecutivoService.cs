using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Api.Analytics;

/// <summary>Recorte do relatório: período mais os três cortes que a diretoria pede.</summary>
public record FiltroRelatorio(DateOnly From, DateOnly To, string? Company, string? CostCenter, Guid? BuyerId);

public record LinhaFamilia(string Family, decimal Value, decimal Quantity, int Orders, double Percent);
public record LinhaSaving(string Buyer, int Processes, decimal Baseline, decimal Closed, decimal Saving,
    double SavingPercent, decimal Spend, int Orders);
/// <summary>Fornecedor por spend, com o acumulado e a classe da curva ABC (A até 80%, B até 95%, C o resto).</summary>
public record LinhaFornecedor(string Supplier, int Orders, decimal Value, double Percent,
    double Cumulative = 0, string Class = "A");
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

/// <summary>Um mês do recorte: o que saiu e o que a negociação segurou naquele mês.</summary>
public record LinhaMes(string Month, decimal Spend, int Orders, int Processes, decimal Saving, double? SavingPercent);

/// <summary>
/// Uma régua do saving: quanto se ganhou, contra que base, em quantos processos. Nula
/// (<c>Processes == 0</c>) quando nenhum processo do recorte a tem — a tela diz "não se aplica"
/// em vez de mostrar zero, porque zero seria "negociou e não ganhou nada".
/// </summary>
public record ReguaDoSaving(int Processes, decimal Baseline, decimal Closed, decimal Saving, double? Percent);

/// <summary>As três réguas lado a lado — respostas a perguntas diferentes, nunca somadas.</summary>
public record ReguasDoSaving(ReguaDoSaving Negotiation, ReguaDoSaving Competition, ReguaDoSaving Budget);

/// <summary>
/// O mesmo recorte na janela imediatamente anterior, do mesmo tamanho. Só os totais: é
/// o que transforma "R$ 12 mil de saving" em "R$ 12 mil, 30% a mais que no mês passado".
/// </summary>
public record PeriodoAnterior(DateOnly From, DateOnly To, decimal Spend, int Orders, decimal SavingTotal,
    double UrgentPercent, double? OtifPercent);

/// <summary>Uma etapa do ciclo: quantas vezes foi medida e a mediana em dias.</summary>
public record TempoDaEtapa(string Stage, string Title, int Measured, double? MedianDays);

/// <summary>
/// Saving de negociação rateado por família ou por fornecedor. O saving mora no processo;
/// quando o processo virou mais de uma O.C., ele é dividido entre elas pelo valor de cada
/// uma — e, dentro da O.C., entre as famílias pelo valor dos itens. Com uma O.C. de uma
/// família só (o caso comum) o rateio é exato.
/// </summary>
public record LinhaSavingRateado(string Label, int Processes, decimal Spend, decimal Saving, double? SavingPercent);

/// <summary>Um item comprado por preço diferente do último pago para o mesmo produto de catálogo.</summary>
public record LinhaReferencia(string Order, string Supplier, string Description, string? CatalogCode,
    decimal Quantity, decimal LastPaidUnitPrice, decimal UnitPrice, decimal Saving);

/// <summary>
/// Saving de referência: o preço fechado contra o <b>último preço pago</b> do mesmo produto de
/// catálogo, congelado no registro da O.C. Nunca se mistura ao saving de negociação — um mede
/// a conversa com o fornecedor, o outro mede a história de preço do produto. Ganho e perda saem
/// separados porque a perda (pagou mais que da última vez) é o que pede ação.
/// </summary>
public record BlocoReferencia(int Orders, int Items, decimal Gain, decimal Loss, decimal Net,
    IReadOnlyList<LinhaReferencia> Rows);

/// <summary>Centro de custo por valor comprado, com o gestor responsável quando o cadastro o tem.</summary>
public record LinhaCentroDeCusto(string Code, string Name, string? Manager, int Orders, decimal Value, double Percent);
/// <summary>Quem pediu: solicitações abertas no período e o valor comprado a partir delas.</summary>
public record LinhaSolicitante(string Requester, int Requisitions, int Orders, decimal Value);
/// <summary>Materiais × serviços, pelo valor dos itens: família SERVICOS ou cotação do tipo serviço é serviço.</summary>
public record EscopoDoGasto(decimal Materials, decimal Services, double MaterialsPercent, double ServicesPercent);
public record OrigemDaDemanda(IReadOnlyList<LinhaCentroDeCusto> CostCenters, IReadOnlyList<LinhaSolicitante> Requesters,
    EscopoDoGasto Scope);

/// <summary>Quem venceu processos com concorrência (dois ou mais proponentes).</summary>
public record LinhaVencedor(string Supplier, int Wins, decimal Value);
/// <summary>
/// As concorrências do recorte: quantos processos, quantos proponentes em média e quem venceu.
/// Proponente é quem mandou proposta — convidado que não respondeu não conta como disputa.
/// </summary>
public record BlocoBids(int Processes, double? AverageProponents, int WithCompetition, IReadOnlyList<LinhaVencedor> Winners);

/// <summary>Spend por condição comercial, com o prazo em dias quando ele se lê da condição.</summary>
public record LinhaPagamento(string Term, int Orders, decimal Value, double Percent, int? Days);
/// <summary>
/// Prazo médio de pagamento ponderado pelo valor (DPO das negociações): o prazo vem da proposta
/// vencedora; sem ele, do texto da condição da O.C. Pedido sem prazo legível fica fora da média.
/// </summary>
public record BlocoPagamento(double? WeightedDays, int OrdersWithDays, decimal ValueWithDays, IReadOnlyList<LinhaPagamento> Terms);

/// <summary>
/// Aderência ao fluxo formal: dos pedidos que já passaram do ponto de registrar a O.C. do ERP
/// (tudo menos a fila em aberto), quantos a têm. A exceção justificada (PO-BR-011) conta como
/// desvio — auditável, mas desvio.
/// </summary>
public record AderenciaDaOc(int Orders, int Formal, decimal Value, decimal FormalValue, double? Percent);

public record RelatorioExecutivo(
    DateOnly From, DateOnly To, FiltroRelatorio Filters, string? CompanyLabel, string? CostCenterLabel,
    string? BuyerLabel, DateTimeOffset GeneratedAt,
    KpisDoRelatorio Kpis, CoberturaDoRelatorio Coverage,
    IReadOnlyList<LinhaFamilia> Families, IReadOnlyList<LinhaSaving> Buyers,
    BlocoFornecedores Suppliers, BlocoUrgencia Urgent, IReadOnlyList<LinhaOtif> Otif,
    BlocoSemOc WithoutErp, OpcoesDeFiltro FilterOptions,
    IReadOnlyList<LinhaMes> Months, ReguasDoSaving SavingRulers, PeriodoAnterior Previous,
    IReadOnlyList<TempoDaEtapa> CycleTimes,
    IReadOnlyList<LinhaSavingRateado> SavingByFamily, IReadOnlyList<LinhaSavingRateado> SavingBySupplier,
    BlocoReferencia Reference,
    OrigemDaDemanda Demand, BlocoBids Bids, BlocoPagamento Payment, AderenciaDaOc Adherence);

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

    /// <summary>
    /// O universo de um recorte: os pedidos vivos do período, filtrados, e o caminho até a
    /// empresa e o centro de custo de cada um. Sai daqui porque o período anterior precisa
    /// do mesmo caminho — comparar com uma janela apurada por outra regra seria comparar nada.
    /// </summary>
    private sealed class Universo
    {
        public required List<PurchaseOrder> Todos { get; init; }
        public required List<PurchaseOrder> Pos { get; init; }
        public required Dictionary<Guid, SolicitacaoDoRelatorio> Prs { get; init; }
        public required Dictionary<string, CentroDoRelatorio> CcPorCodigo { get; init; }
        public required List<EmpresaDoRelatorio> Empresas { get; init; }
        public required List<CentroDoRelatorio> Centros { get; init; }

        public string? CentroDe(PurchaseOrder o) =>
            o.SourcePrId is not null && Prs.TryGetValue(o.SourcePrId.Value, out var pr)
                ? (string.IsNullOrWhiteSpace(pr.CostCenter) ? null : pr.CostCenter) : null;
    }

    private sealed record SolicitacaoDoRelatorio(Guid Id, string Number, string? CostCenter, string? Company,
        string Priority, string? UrgencyReason, string? UrgencyImpact, string? RequesterLabel,
        DateTimeOffset? SubmittedAt, DateTimeOffset CreatedAt);
    /// <summary>Acumulador do rateio do saving por família ou fornecedor.</summary>
    private sealed class Rateio
    {
        public HashSet<Guid> Processos { get; } = [];
        public decimal Spend { get; set; }
        public decimal Saving { get; set; }
        public decimal Baseline { get; set; }
    }

    private sealed record CentroDoRelatorio(string Code, string Name, Guid? CompanyId, bool Active, string? ManagerName = null);
    private sealed record EmpresaDoRelatorio(Guid Id, string LegalName, bool Active);
    private sealed record CotacaoDoRelatorio(Guid Id, Guid SourcePrId, decimal? BaselineValue, decimal? NegotiatedValue,
        decimal? SavingValue, decimal? CompetitionBaselineValue, decimal? CompetitionSaving,
        decimal? BudgetBaselineValue, decimal? BudgetSaving,
        DateTimeOffset? SelectedAt, DateTimeOffset? ManagerApprovedAt, DateTimeOffset? DirectorApprovedAt,
        QuotationKind Kind = QuotationKind.Purchase, Guid? WinnerSupplierId = null);
    private sealed record PropostaDoRelatorio(Guid QuotationId, Guid SupplierId, int VersionNumber,
        int? PaymentDays, string? PaymentTerms);

    private async Task<Universo> UniversoAsync(FiltroRelatorio filtro, CancellationToken ct)
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
                .Select(r => new SolicitacaoDoRelatorio(r.Id, r.Number, r.CostCenter, r.Company, r.Priority,
                    r.UrgencyReason, r.UrgencyImpact, r.RequesterLabel, r.SubmittedAt, r.CreatedAt))
                .ToListAsync(ct))
            .ToDictionary(x => x.Id);

        var centros = await db.CostCenters.Select(c => new CentroDoRelatorio(c.Code, c.Name, c.CompanyId, c.Active, c.ManagerName)).ToListAsync(ct);
        var ccPorCodigo = centros.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
        var empresas = await db.Companies.Select(c => new EmpresaDoRelatorio(c.Id, c.LegalName, c.Active)).ToListAsync(ct);
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

        return new Universo { Todos = todos, Pos = pos, Prs = prs, CcPorCodigo = ccPorCodigo, Empresas = empresas, Centros = centros };
    }

    /// <summary>
    /// As cotações por trás dos pedidos, com o que o relatório lê delas. O saving mora no
    /// processo, não no pedido — e um processo dividido por família gera mais de um pedido,
    /// então quem consome isto conta cada cotação **uma vez**.
    /// </summary>
    private async Task<Dictionary<Guid, CotacaoDoRelatorio>> CotacoesDeAsync(IEnumerable<PurchaseOrder> pos, CancellationToken ct)
    {
        var ids = pos.Where(o => o.QuotationId is not null).Select(o => o.QuotationId!.Value).Distinct().ToList();
        return (await db.Quotations.Where(q => ids.Contains(q.Id))
                .Select(q => new CotacaoDoRelatorio(q.Id, q.SourcePrId, q.BaselineValue, q.NegotiatedValue, q.SavingValue,
                    q.CompetitionBaselineValue, q.CompetitionSaving, q.BudgetBaselineValue, q.BudgetSaving,
                    q.SelectedAt, q.ManagerApprovedAt, q.DirectorApprovedAt, q.Kind, q.WinnerSupplierId))
                .ToListAsync(ct))
            .ToDictionary(x => x.Id);
    }

    private async Task<List<PropostaDoRelatorio>> PropostasDeAsync(IReadOnlyCollection<Guid> cotacaoIds, CancellationToken ct) =>
        cotacaoIds.Count == 0 ? [] : await db.Proposals.Where(p => cotacaoIds.Contains(p.QuotationId))
            .Select(p => new PropostaDoRelatorio(p.QuotationId, p.SupplierId, p.VersionNumber, p.PaymentDays, p.PaymentTerms))
            .ToListAsync(ct);

    /// <summary>Cada cotação uma vez, na ordem do primeiro pedido que a fechou.</summary>
    private static IEnumerable<(PurchaseOrder Pedido, CotacaoDoRelatorio Cotacao)> CotacoesUnicas(
        IEnumerable<PurchaseOrder> pos, Dictionary<Guid, CotacaoDoRelatorio> cotacoes)
    {
        var vistas = new HashSet<Guid>();
        foreach (var o in pos.OrderBy(o => o.CreatedAt))
            if (o.QuotationId is { } qid && vistas.Add(qid) && cotacoes.TryGetValue(qid, out var q))
                yield return (o, q);
    }

    private static double? Percentual(decimal parte, decimal total) =>
        total > 0 ? Math.Round((double)(parte * 100 / total), 1) : null;

    private static double? OtifDe(IReadOnlyCollection<PurchaseOrder> pos)
    {
        var medidos = pos.Count(o => o.Otif is not null);
        return medidos > 0 ? Math.Round(pos.Count(o => o.Otif == true) * 100.0 / medidos, 1) : null;
    }

    /// <summary>
    /// O prazo em dias lido da condição comercial: "à vista" é zero, "30 dias" e "28D" são o número.
    /// Condição sem número (ou "30/60/90") entra pelo primeiro número — a leitura conservadora — e
    /// texto sem número nenhum fica sem prazo.
    /// </summary>
    public static int? DiasDe(string? condicao)
    {
        if (string.IsNullOrWhiteSpace(condicao)) return null;
        var texto = condicao.Trim().ToLowerInvariant();
        if (texto.Contains("vista") || texto.Contains("antecip")) return 0;
        var m = System.Text.RegularExpressions.Regex.Match(texto, @"\d+");
        return m.Success && int.TryParse(m.Value, out var d) ? d : null;
    }

    private static double? MedianaDias(IReadOnlyList<double> dias)
    {
        if (dias.Count == 0) return null;
        var ordenado = dias.OrderBy(d => d).ToList();
        var meio = ordenado.Count / 2;
        var mediana = ordenado.Count % 2 == 1 ? ordenado[meio] : (ordenado[meio - 1] + ordenado[meio]) / 2;
        return Math.Round(mediana, 1);
    }

    public async Task<RelatorioExecutivo> GerarAsync(FiltroRelatorio filtro, CancellationToken ct = default)
    {
        var u = await UniversoAsync(filtro, ct);
        var todos = u.Todos;
        var pos = u.Pos;
        var prs = u.Prs;
        var ccPorCodigo = u.CcPorCodigo;
        var centros = u.Centros;
        var empresas = u.Empresas;
        string? CentroDe(PurchaseOrder o) => u.CentroDe(o);

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
        var cotacoes = await CotacoesDeAsync(pos, ct);

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
        // curva ABC: o acumulado diz quantos fornecedores respondem por 80% do gasto
        decimal acumulado = 0;
        fornecedores = fornecedores.Select(f =>
        {
            acumulado += f.Value;
            var cum = Math.Round((double)Fatia(acumulado), 1);
            return f with { Cumulative = cum, Class = cum <= 80 ? "A" : cum <= 95 ? "B" : "C" };
        }).ToList();
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

        // ---- 7. saving mês a mês ------------------------------------------------
        // O pedido conta no mês em que foi criado; a cotação conta uma vez, no mês do
        // primeiro pedido que a fechou — a mesma regra do bloco por comprador, senão os
        // dois blocos somariam savings diferentes para o mesmo período. Mês sem pedido
        // aparece zerado: a linha do tempo não pula mês.
        var unicas = CotacoesUnicas(pos, cotacoes).ToList();
        static string MesDe(DateTimeOffset d) => d.UtcDateTime.ToString("yyyy-MM");
        var meses = new List<string>();
        for (var m = new DateOnly(filtro.From.Year, filtro.From.Month, 1); m <= filtro.To; m = m.AddMonths(1))
            meses.Add(m.ToString("yyyy-MM"));
        var months = meses.Select(mes =>
        {
            var doMes = pos.Where(o => MesDe(o.CreatedAt) == mes).ToList();
            var cotacoesDoMes = unicas.Where(x => MesDe(x.Pedido.CreatedAt) == mes).ToList();
            var saving = cotacoesDoMes.Sum(x => x.Cotacao.SavingValue ?? 0);
            var baseline = cotacoesDoMes.Sum(x => x.Cotacao.BaselineValue ?? 0);
            return new LinhaMes(mes, doMes.Sum(o => o.TotalValue), doMes.Count, cotacoesDoMes.Count,
                saving, Percentual(saving, baseline));
        }).ToList();

        // ---- 8. as três réguas do saving ----------------------------------------
        // Três perguntas, três números com nome (ver `Quotation`): negociação (contra a
        // primeira proposta do vencedor), concorrência (contra a maior proposta completa
        // do BID) e orçamento (contra o que o solicitante previu). Cada régua só conta o
        // processo que a tem — e nunca se somam.
        ReguaDoSaving Regua(Func<CotacaoDoRelatorio, decimal?> saving, Func<CotacaoDoRelatorio, decimal?> baseline)
        {
            var com = unicas.Select(x => x.Cotacao).Where(q => saving(q) is not null).ToList();
            var ganho = com.Sum(q => saving(q)!.Value);
            var basePeriodo = com.Sum(q => baseline(q) ?? 0);
            return new ReguaDoSaving(com.Count, basePeriodo, basePeriodo - ganho, ganho, Percentual(ganho, basePeriodo));
        }
        var savingRulers = new ReguasDoSaving(
            Regua(q => q.SavingValue, q => q.BaselineValue),
            Regua(q => q.CompetitionSaving, q => q.CompetitionBaselineValue),
            Regua(q => q.BudgetSaving, q => q.BudgetBaselineValue));

        // ---- 9. tempo do ciclo --------------------------------------------------
        // Mediana, não média: um processo que ficou três meses parado numa aprovação
        // arrastaria a média e esconderia que os outros vinte andaram em uma semana.
        // Cada etapa conta pelo seu próprio relógio, e só quando as duas marcas existem.
        static double Dias(DateTimeOffset de, DateTimeOffset ate) => (ate - de).TotalDays;
        var porCotacao = unicas.Select(x => new
        {
            x.Pedido, x.Cotacao,
            Pedida = prs.TryGetValue(x.Cotacao.SourcePrId, out var pr) ? pr.SubmittedAt ?? pr.CreatedAt : (DateTimeOffset?)null,
            Aprovada = x.Cotacao.DirectorApprovedAt ?? x.Cotacao.ManagerApprovedAt,
        }).ToList();
        TempoDaEtapa Etapa(string chave, string titulo, IEnumerable<double?> medidas)
        {
            var validas = medidas.Where(d => d is >= 0).Select(d => d!.Value).ToList();
            return new TempoDaEtapa(chave, titulo, validas.Count, MedianaDias(validas));
        }
        var cycleTimes = new List<TempoDaEtapa>
        {
            Etapa("solicitacao_escolha", "Solicitação → escolha do fornecedor",
                porCotacao.Select(x => x.Pedida is { } p && x.Cotacao.SelectedAt is { } e ? Dias(p, e) : (double?)null)),
            Etapa("escolha_aprovacao", "Escolha → aprovação final",
                porCotacao.Select(x => x.Cotacao.SelectedAt is { } e && x.Aprovada is { } a ? Dias(e, a) : (double?)null)),
            Etapa("aprovacao_oc", "Aprovação → O.C.",
                porCotacao.Select(x => x.Aprovada is { } a ? Dias(a, x.Pedido.CreatedAt) : (double?)null)),
            Etapa("oc_recebimento", "O.C. → recebimento",
                pos.Select(o => (o.DeliveryCompletedAt ?? o.ReceivedAt) is { } r ? Dias(o.CreatedAt, r) : (double?)null)),
            Etapa("solicitacao_oc", "Solicitação → O.C. (total)",
                pos.Select(o => o.SourcePrId is { } id && prs.TryGetValue(id, out var pr)
                    ? Dias(pr.SubmittedAt ?? pr.CreatedAt, o.CreatedAt) : (double?)null)),
        };

        // ---- 10. saving por família e por fornecedor ----------------------------
        // O saving é do processo. Para chegar à família e ao fornecedor ele é rateado: entre
        // as O.C.s do processo pelo valor de cada uma, e dentro da O.C. entre as famílias pelo
        // valor dos itens. Uma O.C. de uma família só — o caso comum — sai exata.
        var porFamilia = new Dictionary<string, Rateio>();
        var porFornecedor = new Dictionary<string, Rateio>();
        static void Acumular(Dictionary<string, Rateio> mapa, string chave, Guid processo,
            decimal spendParte, decimal savingParte, decimal baselineParte)
        {
            if (!mapa.TryGetValue(chave, out var r)) mapa[chave] = r = new Rateio();
            r.Processos.Add(processo);
            r.Spend += spendParte; r.Saving += savingParte; r.Baseline += baselineParte;
        }
        foreach (var (_, q) in unicas)
        {
            var pedidosDoProcesso = pos.Where(o => o.QuotationId == q.Id).ToList();
            var totalDoProcesso = pedidosDoProcesso.Sum(o => o.TotalValue);
            foreach (var o in pedidosDoProcesso)
            {
                var fatia = totalDoProcesso > 0 ? o.TotalValue / totalDoProcesso : 1m / pedidosDoProcesso.Count;
                var savingDaOc = (q.SavingValue ?? 0) * fatia;
                var baselineDaOc = (q.BaselineValue ?? 0) * fatia;
                Acumular(porFornecedor, string.IsNullOrWhiteSpace(o.SupplierName) ? "—" : o.SupplierName,
                    q.Id, o.TotalValue, savingDaOc, baselineDaOc);

                var valorDosItens = o.Items.Sum(i => (i.UnitPrice ?? 0) * i.Quantity);
                foreach (var g in o.Items.GroupBy(FamiliaDe))
                {
                    var valor = g.Sum(i => (i.UnitPrice ?? 0) * i.Quantity);
                    var parte = valorDosItens > 0 ? valor / valorDosItens : 1m / o.Items.Count;
                    Acumular(porFamilia, g.Key, q.Id, valor, savingDaOc * parte, baselineDaOc * parte);
                }
            }
        }
        static List<LinhaSavingRateado> Linhas(Dictionary<string, Rateio> mapa) =>
            mapa.Select(kv => new LinhaSavingRateado(kv.Key, kv.Value.Processos.Count,
                    Math.Round(kv.Value.Spend, 2), Math.Round(kv.Value.Saving, 2),
                    Percentual(kv.Value.Saving, kv.Value.Baseline)))
                .OrderByDescending(l => l.Saving).ThenByDescending(l => l.Spend).ToList();
        var savingByFamily = Linhas(porFamilia);
        var savingBySupplier = Linhas(porFornecedor);

        // ---- 11. saving de referência (× último preço pago) -----------------------
        // Congelado item a item no registro da O.C.; aqui só se soma. Ganho e perda saem
        // separados: "pagou mais que da última vez" é o que a diretoria quer ver, e um
        // líquido positivo esconderia isso.
        var itensComReferencia = pos.SelectMany(o => o.Items
                .Where(i => i.ReferenceSaving is not null && i.LastPaidUnitPrice is not null)
                .Select(i => (Pedido: o, Item: i)))
            .ToList();
        var ganho = itensComReferencia.Where(x => x.Item.ReferenceSaving > 0).Sum(x => x.Item.ReferenceSaving!.Value);
        var perda = itensComReferencia.Where(x => x.Item.ReferenceSaving < 0).Sum(x => x.Item.ReferenceSaving!.Value);
        var reference = new BlocoReferencia(
            itensComReferencia.Select(x => x.Pedido.Id).Distinct().Count(), itensComReferencia.Count,
            ganho, perda, ganho + perda,
            itensComReferencia.OrderBy(x => x.Item.ReferenceSaving).Take(25)      // a perda primeiro
                .Select(x => new LinhaReferencia(x.Pedido.Number, x.Pedido.SupplierName, x.Item.Description,
                    x.Item.CatalogCode, x.Item.Quantity, x.Item.LastPaidUnitPrice!.Value, x.Item.UnitPrice ?? 0,
                    x.Item.ReferenceSaving!.Value))
                .ToList());

        // ---- 12. origem da demanda: quem pediu, de onde, material ou serviço ----------
        var porCentro = pos.GroupBy(o => CentroDe(o) ?? "—")
            .Select(g =>
            {
                var cc = ccPorCodigo.GetValueOrDefault(g.Key);
                return new LinhaCentroDeCusto(g.Key, cc?.Name ?? (g.Key == "—" ? "sem solicitação de origem" : g.Key),
                    cc?.ManagerName, g.Count(), g.Sum(o => o.TotalValue), Pct(g.Sum(o => o.TotalValue)));
            })
            .OrderByDescending(c => c.Value).Take(5).ToList();
        var porSolicitante = pos.Where(o => o.SourcePrId is { } id && prs.ContainsKey(id))
            .GroupBy(o => string.IsNullOrWhiteSpace(prs[o.SourcePrId!.Value].RequesterLabel) ? "—" : prs[o.SourcePrId!.Value].RequesterLabel!)
            .Select(g => new LinhaSolicitante(g.Key, g.Select(o => o.SourcePrId).Distinct().Count(), g.Count(), g.Sum(o => o.TotalValue)))
            .OrderByDescending(x => x.Value).Take(5).ToList();
        bool EhServico(PurchaseOrder o, PurchaseOrderItem i) =>
            FamiliaDe(i) == ProductTypes.Servicos
            || (o.QuotationId is { } qid && cotacoes.TryGetValue(qid, out var qc) && qc.Kind == QuotationKind.Service);
        var itensDoRecorte = pos.SelectMany(o => o.Items.Select(i => (Pedido: o, Item: i, Valor: (i.UnitPrice ?? 0) * i.Quantity))).ToList();
        var servicos = itensDoRecorte.Where(x => EhServico(x.Pedido, x.Item)).Sum(x => x.Valor);
        var materiais = itensDoRecorte.Sum(x => x.Valor) - servicos;
        var totalItens = materiais + servicos;
        var demand = new OrigemDaDemanda(porCentro, porSolicitante, new EscopoDoGasto(materiais, servicos,
            totalItens > 0 ? Math.Round((double)(materiais * 100 / totalItens), 1) : 0,
            totalItens > 0 ? Math.Round((double)(servicos * 100 / totalItens), 1) : 0));

        // ---- 13. concorrências (BIDs): proponentes por processo e quem venceu ----------
        // Proponente é quem mandou proposta; convite sem resposta não é disputa. Vencedor de
        // concorrência é o fornecedor da O.C. de um processo com dois ou mais proponentes.
        var propostas = await PropostasDeAsync(unicas.Select(x => x.Cotacao.Id).ToList(), ct);
        var proponentesPorProcesso = propostas.GroupBy(p => p.QuotationId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.SupplierId).Distinct().Count());
        var comConcorrencia = unicas.Where(x => proponentesPorProcesso.GetValueOrDefault(x.Cotacao.Id) >= 2).ToList();
        var vencedores = comConcorrencia
            .SelectMany(x => pos.Where(o => o.QuotationId == x.Cotacao.Id))
            .GroupBy(o => string.IsNullOrWhiteSpace(o.SupplierName) ? "—" : o.SupplierName)
            .Select(g => new LinhaVencedor(g.Key, g.Select(o => o.QuotationId).Distinct().Count(), g.Sum(o => o.TotalValue)))
            .OrderByDescending(v => v.Value).Take(5).ToList();
        var bids = new BlocoBids(unicas.Count,
            unicas.Count > 0 ? Math.Round(unicas.Average(x => proponentesPorProcesso.GetValueOrDefault(x.Cotacao.Id)), 1) : null,
            comConcorrencia.Count, vencedores);

        // ---- 14. formas e prazos de pagamento --------------------------------------------
        // O prazo vem da proposta vencedora (a última versão do fornecedor escolhido); sem ela,
        // do texto da condição gravada na O.C. O DPO é ponderado pelo valor: um pedido de um
        // milhão a 60 dias pesa mais que dez de mil à vista.
        var vencedoraPorProcesso = propostas
            .Where(p => cotacoes.TryGetValue(p.QuotationId, out var qc) && qc.WinnerSupplierId == p.SupplierId)
            .GroupBy(p => p.QuotationId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.VersionNumber).First());
        int? DiasDoPedido(PurchaseOrder o) =>
            o.QuotationId is { } qid && vencedoraPorProcesso.TryGetValue(qid, out var v)
                ? v.PaymentDays ?? DiasDe(v.PaymentTerms) ?? DiasDe(o.PaymentTerms)
                : DiasDe(o.PaymentTerms);
        var comPrazo = pos.Select(o => (Pedido: o, Dias: DiasDoPedido(o))).Where(x => x.Dias is not null).ToList();
        var valorComPrazo = comPrazo.Sum(x => x.Pedido.TotalValue);
        var terms = pos.GroupBy(o => string.IsNullOrWhiteSpace(o.PaymentTerms) ? "não informada" : o.PaymentTerms.Trim())
            .Select(g => new LinhaPagamento(g.Key, g.Count(), g.Sum(o => o.TotalValue), Pct(g.Sum(o => o.TotalValue)),
                g.Key == "não informada" ? null : g.Select(DiasDoPedido).FirstOrDefault(d => d is not null)))
            .OrderByDescending(t => t.Value).ToList();
        var payment = new BlocoPagamento(
            valorComPrazo > 0 ? Math.Round((double)(comPrazo.Sum(x => x.Pedido.TotalValue * x.Dias!.Value) / valorComPrazo), 1) : null,
            comPrazo.Count, valorComPrazo, terms);

        // ---- 15. aderência ao fluxo formal da O.C. ---------------------------------------
        var julgados = pos.Where(o => !aRegistrar.Contains(o)).ToList();
        var formais = julgados.Where(o => !string.IsNullOrWhiteSpace(o.ErpNumber)).ToList();
        var adherence = new AderenciaDaOc(julgados.Count, formais.Count,
            julgados.Sum(o => o.TotalValue), formais.Sum(o => o.TotalValue),
            julgados.Count > 0 ? Math.Round(formais.Count * 100.0 / julgados.Count, 1) : null);

        // ---- período anterior ---------------------------------------------------
        // A janela imediatamente antes, do mesmo tamanho, com os mesmos filtros. Só os
        // totais: comparação é contexto para o KPI, não um segundo relatório.
        var dias = filtro.To.DayNumber - filtro.From.DayNumber + 1;
        var anteriorFiltro = filtro with { To = filtro.From.AddDays(-1), From = filtro.From.AddDays(-dias) };
        var ua = await UniversoAsync(anteriorFiltro, ct);
        var cotacoesAnteriores = await CotacoesDeAsync(ua.Pos, ct);
        var spendAnterior = ua.Pos.Sum(o => o.TotalValue);
        var urgenteAnterior = ua.Pos.Where(o => o.SourcePrId is not null && ua.Prs.TryGetValue(o.SourcePrId.Value, out var pr)
                                                 && pr.Priority == "URGENT").Sum(o => o.TotalValue);
        var previous = new PeriodoAnterior(anteriorFiltro.From, anteriorFiltro.To, spendAnterior, ua.Pos.Count,
            CotacoesUnicas(ua.Pos, cotacoesAnteriores).Sum(x => x.Cotacao.SavingValue ?? 0),
            spendAnterior > 0 ? Math.Round((double)(urgenteAnterior * 100 / spendAnterior), 1) : 0,
            OtifDe(ua.Pos));

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
            kpis, coverage, families, buyers, suppliers, urgent, otif, withoutErp, filterOptions,
            months, savingRulers, previous, cycleTimes, savingByFamily, savingBySupplier, reference,
            demand, bids, payment, adherence);
    }
}
