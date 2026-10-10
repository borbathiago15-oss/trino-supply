using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Materials;

namespace TrinoSupply.Foundation.Api.Analytics;

/// <summary>Uma linha de ranking: quantas solicitações, quanto foi pedido e quanto foi entregue.</summary>
public record LinhaDeMaterial(string Label, string Key, int Count, decimal Qty, decimal Delivered);

public record MesDeMaterial(string Month, int Requested, int Fulfilled, int Rejected);

/// <summary>Como o atendimento de uma família foi contra o prazo dela, no período.</summary>
public record PrazoDaFamilia(string Family, int? MaxDays, int Measured, int Met, double? AvgDays);

public record KpisDeMaterial(
    int Requested, int RequestedPrev, decimal RequestedQty, decimal DeliveredQty,
    int AwaitingApproval, int InWarehouseQueue, int Fulfilled, int Partial, int Rejected, int Cancelled,
    int PurchaseRouteItems, double? AvgApprovalHours, double? AvgFulfillDays, double? AvgTotalDays,
    double? SlaMetPercent, int SlaMeasured, int SlaBreachedOpen);

public record CentroDeMaterial(string Code, string Name);
public record OpcoesDeMaterial(IReadOnlyList<CentroDeMaterial> CostCenters, IReadOnlyList<string> Families);

public record RelatorioDeMaterial(
    DateOnly From, DateOnly To, KpisDeMaterial Kpis, IReadOnlyList<MesDeMaterial> Months,
    IReadOnlyList<LinhaDeMaterial> ByCostCenter, IReadOnlyList<LinhaDeMaterial> ByFamily,
    IReadOnlyList<LinhaDeMaterial> ByProduct, IReadOnlyList<LinhaDeMaterial> ByRequester,
    IReadOnlyList<PrazoDaFamilia> SlaByFamily, OpcoesDeMaterial FilterOptions,
    IReadOnlyDictionary<string, string> Indicators);

/// <summary>
/// A solicitação de material no Dashboard e na Visão da diretoria: quanto se pediu ao
/// almoxarifado, quanto espera, quanto foi atendido e em quanto tempo — por centro de custo,
/// família, produto e solicitante.
///
/// <para>
/// É o <b>outro cano</b> da casa, ao lado da compra: o material que já está em estoque e sai
/// pela porta do almoxarifado. O Dashboard contava só a compra, e a diretoria não tinha como
/// perguntar "quanto fardamento o centro X pediu este mês" sem abrir o Painel de Atendimentos.
/// Os filtros são <b>próprios</b> (período, centro, família, produto): os da compra falam de
/// fornecedor, comprador e prioridade, que a solicitação de material não tem.
/// </para>
///
/// <para>
/// Três réguas vêm de onde já existem, de propósito, para o número bater com a tela que o
/// almoxarife usa: o <b>prazo de atendimento</b> é o de <see cref="PrazoDeAtendimentoService"/>
/// (o mais curto entre as famílias dos itens, contado da liberação do Nível 1); o recorte por
/// centro é o de <see cref="RecorteDoPainel"/>; e a família do item vem do <b>catálogo</b>, porque
/// o item da solicitação guarda código e descrição, não a família.
/// </para>
///
/// <para>
/// <b>Pendente é pergunta de agora</b>, não do período: o que espera aprovação e o que está na
/// fila do estoque contam o que está aberto hoje, dentro do recorte de centro, família e produto.
/// Contá-los só pelo período esconderia a solicitação de setembro que ainda ninguém atendeu, que
/// é justamente a que a diretoria quer ver. "Atendidas" e "solicitadas" são do período.
/// </para>
///
/// <para>
/// O filtro por família ou produto <b>entra pelo item</b>: a solicitação conta se algum item
/// dela entra, e as quantidades somam só os itens que entram. Uma solicitação com luva e papel
/// filtrada por EPI é uma solicitação, com a quantidade da luva.
/// </para>
/// </summary>
public class AnalyticsDeMaterialService(AppDbContext db, TimeProvider clock, PrazoDeAtendimentoService prazos)
{
    private const int Cap = 5000;
    private const int Top = 10;

    /// <summary>Quem lê a compra lê o material; o almoxarife também, porque é a fila dele.</summary>
    public static bool CanView(string role) =>
        AnalyticsService.CanViewSupply(role) || role == Roles.WarehouseOperator;

    /// <summary>De onde cada número sai — a frase que o ⓘ do cartão abre.</summary>
    public static readonly IReadOnlyDictionary<string, string> Definicoes = new Dictionary<string, string>
    {
        ["requested"] = "Solicitações de material criadas no período, no recorte escolhido.",
        ["awaitingApproval"] = "Solicitações que esperam a aprovação do responsável do centro hoje, independentemente do período.",
        ["inWarehouseQueue"] = "Solicitações já aprovadas que o almoxarifado ainda não atendeu, hoje.",
        ["fulfilled"] = "Solicitações atendidas (total ou em parte) no período, pela data do atendimento.",
        ["partial"] = "Atendimentos do período em que faltou material: o que faltou virou solicitação de compra.",
        ["avgApprovalHours"] = "Horas entre a criação e a aprovação do responsável do centro, nas solicitações aprovadas no período.",
        ["avgFulfillDays"] = "Dias entre a aprovação e o atendimento do almoxarifado, nas solicitações atendidas no período.",
        ["avgTotalDays"] = "Dias entre a criação e o atendimento, nas solicitações atendidas no período.",
        ["slaMetPercent"] = "Parte das solicitações atendidas no período dentro do prazo de atendimento da família (Cadastros → Prazo de atendimento). Família com prazo zero fica fora da conta.",
        ["slaBreachedOpen"] = "Solicitações na fila do almoxarifado hoje com o prazo de atendimento estourado.",
    };

    /// <param name="costCenters">
    /// O recorte por <b>unidade</b> da parede: os centros de custo daquela empresa, em caixa alta.
    /// Vazio é "nenhum centro" (unidade sem centro cadastrado), e nulo é a empresa inteira. É
    /// outro recorte que o de <paramref name="costCenter"/>, que é um centro escolhido no filtro.
    /// </param>
    public async Task<RelatorioDeMaterial> MaterialAsync(
        DateOnly from, DateOnly to, string? costCenter, string? family, string? product,
        IReadOnlyCollection<string>? costCenters = null, CancellationToken ct = default)
    {
        if (to < from) (from, to) = (to, from);
        var fromDt = MeiaNoite(from);
        var toDt = MeiaNoite(to.AddDays(1));
        // a janela anterior tem o mesmo tamanho: é a base da variação
        var dias = to.DayNumber - from.DayNumber + 1;
        var prevFromDt = MeiaNoite(from.AddDays(-dias));

        // filtro na entidade: o criado desde a janela anterior, o que ainda está aberto (pendência
        // é pergunta de agora) e o atendido na janela; projeção por último
        var consulta = db.MaterialRequisitions.AsNoTracking().Include(r => r.Items)
            .Where(r => r.CreatedAt < toDt)
            .Where(r => r.CreatedAt >= prevFromDt
                        || r.Status == MaterialRequisitionStatus.Submitted
                        || r.Status == MaterialRequisitionStatus.Approved
                        || (r.FulfilledAt != null && r.FulfilledAt >= fromDt));
        if (!string.IsNullOrWhiteSpace(costCenter))
        {
            var centro = costCenter.Trim().ToUpperInvariant();
            consulta = consulta.Where(r => r.CostCenter.ToUpper() == centro);
        }
        if (costCenters is not null)
        {
            // `Contains` com array e filtro na entidade: o que o Npgsql traduz
            var daUnidade = costCenters.Select(c => c.Trim().ToUpperInvariant()).Distinct().ToArray();
            consulta = consulta.Where(r => daUnidade.Contains(r.CostCenter.ToUpper()));
        }
        var mrs = await consulta.OrderByDescending(r => r.CreatedAt).Take(Cap).ToListAsync(ct);

        // a família e o nome atual do produto vêm do catálogo; `Contains` com array
        var ids = mrs.SelectMany(r => r.Items.Select(i => i.CatalogItemId)).Distinct().ToArray();
        var produtos = (await db.CatalogItems.AsNoTracking()
                .Where(i => ids.Contains(i.Id))
                .Select(i => new { i.Id, i.Code, i.Description, i.Family }).ToListAsync(ct))
            .ToDictionary(x => x.Id);
        string FamiliaDe(Guid id) =>
            produtos.TryGetValue(id, out var p) && !string.IsNullOrWhiteSpace(p.Family) ? p.Family : "SEM FAMÍLIA";

        var fam = string.IsNullOrWhiteSpace(family) ? null : family.Trim();
        var prod = string.IsNullOrWhiteSpace(product) ? null : product.Trim();
        bool ItemEntra(MaterialRequisitionItem i) =>
            (fam is null || string.Equals(FamiliaDe(i.CatalogItemId), fam, StringComparison.OrdinalIgnoreCase))
            && (prod is null
                || i.CatalogCode.Contains(prod, StringComparison.OrdinalIgnoreCase)
                || i.Description.Contains(prod, StringComparison.OrdinalIgnoreCase));
        if (fam is not null || prod is not null) mrs = mrs.Where(r => r.Items.Any(ItemEntra)).ToList();
        IEnumerable<MaterialRequisitionItem> Itens(MaterialRequisition r) => r.Items.Where(ItemEntra);

        bool NaJanela(DateTimeOffset? d) => d is { } x && x >= fromDt && x < toDt;
        bool Atendida(MaterialRequisition r) =>
            r.Status is MaterialRequisitionStatus.Fulfilled or MaterialRequisitionStatus.PartiallyFulfilled
            && NaJanela(r.FulfilledAt);

        var criadas = mrs.Where(r => NaJanela(r.CreatedAt)).ToList();
        var criadasAntes = mrs.Count(r => r.CreatedAt >= prevFromDt && r.CreatedAt < fromDt);
        var atendidas = mrs.Where(Atendida).ToList();
        var esperandoAprovacao = mrs.Where(r => r.Status == MaterialRequisitionStatus.Submitted).ToList();
        var naFila = mrs.Where(r => r.Status == MaterialRequisitionStatus.Approved).ToList();
        var parciais = mrs.Count(r => r.Status is MaterialRequisitionStatus.PartiallyFulfilled or MaterialRequisitionStatus.PurchaseRoute
                                      && NaJanela(r.FulfilledAt));
        var rejeitadas = mrs.Count(r => r.Status == MaterialRequisitionStatus.Rejected && NaJanela(r.ApprovedAt));
        var canceladas = mrs.Count(r => r.Status == MaterialRequisitionStatus.Cancelled && NaJanela(r.UpdatedAt));
        var itensParaCompra = mrs.Where(r => NaJanela(r.FulfilledAt)).SelectMany(Itens)
            .Count(i => i.Status is MaterialItemStatus.PurchaseRoute or MaterialItemStatus.PartiallyFulfilled);

        static double? Media(IEnumerable<double> xs)
        {
            var lista = xs.ToList();
            return lista.Count == 0 ? null : Math.Round(lista.Average(), 1);
        }
        var aprovadasNaJanela = mrs.Where(r => NaJanela(r.ApprovedAt) && r.Status != MaterialRequisitionStatus.Rejected).ToList();
        var mediaAprovacao = Media(aprovadasNaJanela.Select(r => (r.ApprovedAt!.Value - r.CreatedAt).TotalHours));
        var mediaAtendimento = Media(atendidas.Where(r => r.ApprovedAt != null)
            .Select(r => (r.FulfilledAt!.Value - r.ApprovedAt!.Value).TotalDays));
        var mediaTotal = Media(atendidas.Select(r => (r.FulfilledAt!.Value - r.CreatedAt).TotalDays));

        // o prazo de atendimento é a régua da fila do almoxarifado — a mesma função
        var situacoes = await prazos.SituacoesAsync([.. atendidas, .. naFila], ct);
        var medidas = atendidas.Select(r => situacoes.GetValueOrDefault(r.Id))
            .Where(s => s is { Status: not null }).Select(s => s!).ToList();
        double? noPrazo = medidas.Count == 0 ? null
            : Math.Round(100.0 * medidas.Count(s => !s.Breached) / medidas.Count, 1);
        var estouradasAbertas = naFila.Count(r => situacoes.GetValueOrDefault(r.Id)?.Breached == true);

        var ccNome = (await db.CostCenters.AsNoTracking().Select(c => new { c.Code, c.Name }).ToListAsync(ct))
            .GroupBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
        LinhaDeMaterial Linha(string label, string key, IReadOnlyList<MaterialRequisition> grupo) => new(
            label, key, grupo.Count,
            grupo.SelectMany(Itens).Sum(i => i.Quantity), grupo.SelectMany(Itens).Sum(i => i.FulfilledQuantity));

        var porCentro = criadas.GroupBy(r => r.CostCenter)
            .Select(g => Linha(ccNome.TryGetValue(g.Key, out var nome) ? $"{g.Key} — {nome}" : g.Key, g.Key, g.ToList()))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Label, StringComparer.Ordinal).Take(Top).ToList();
        // por id, nunca pelo nome: dois homônimos são duas pessoas
        var porSolicitante = criadas.GroupBy(r => r.RequesterId)
            .Select(g => Linha(g.Last().RequesterLabel, g.Key.ToString(), g.ToList()))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Label, StringComparer.CurrentCulture).Take(Top).ToList();

        var itensCriados = criadas.SelectMany(r => Itens(r).Select(i => (Mr: r, Item: i))).ToList();
        var porFamilia = itensCriados.GroupBy(x => FamiliaDe(x.Item.CatalogItemId))
            .Select(g => new LinhaDeMaterial(g.Key, g.Key, g.Select(x => x.Mr.Id).Distinct().Count(),
                g.Sum(x => x.Item.Quantity), g.Sum(x => x.Item.FulfilledQuantity)))
            .OrderByDescending(x => x.Qty).ThenBy(x => x.Label, StringComparer.Ordinal).Take(Top).ToList();
        var porProduto = itensCriados.GroupBy(x => x.Item.CatalogItemId)
            .Select(g =>
            {
                var p = produtos.GetValueOrDefault(g.Key);
                var codigo = p?.Code ?? g.First().Item.CatalogCode;
                var descricao = p?.Description ?? g.First().Item.Description;
                return new LinhaDeMaterial($"[{codigo}] {descricao}", g.Key.ToString(),
                    g.Select(x => x.Mr.Id).Distinct().Count(), g.Sum(x => x.Item.Quantity), g.Sum(x => x.Item.FulfilledQuantity));
            })
            .OrderByDescending(x => x.Qty).ThenBy(x => x.Label, StringComparer.Ordinal).Take(Top).ToList();

        var meses = new List<MesDeMaterial>();
        var cursor = new DateOnly(from.Year, from.Month, 1);
        var ultimo = new DateOnly(to.Year, to.Month, 1);
        while (cursor <= ultimo)
        {
            var m0 = MeiaNoite(cursor);
            var m1 = MeiaNoite(cursor.AddMonths(1));
            bool NoMes(DateTimeOffset? d) => d is { } x && x >= m0 && x < m1;
            meses.Add(new MesDeMaterial(cursor.ToString("yyyy-MM"),
                mrs.Count(r => NoMes(r.CreatedAt)),
                mrs.Count(r => r.Status is MaterialRequisitionStatus.Fulfilled or MaterialRequisitionStatus.PartiallyFulfilled && NoMes(r.FulfilledAt)),
                mrs.Count(r => r.Status == MaterialRequisitionStatus.Rejected && NoMes(r.ApprovedAt))));
            cursor = cursor.AddMonths(1);
        }

        // a meta por família e o realizado das atendidas: a solicitação com itens de duas
        // famílias conta nas duas, porque o prazo é de cada uma
        var mapa = await prazos.MapaAsync(ct);
        var prazoPorFamilia = atendidas
            .SelectMany(r => Itens(r).Select(i => FamiliaDe(i.CatalogItemId)).Distinct().Select(f => (Familia: f, Mr: r)))
            .GroupBy(x => x.Familia)
            .Select(g =>
            {
                var diasDeCada = g.Where(x => x.Mr.ApprovedAt != null)
                    .Select(x => (x.Mr.FulfilledAt!.Value - x.Mr.ApprovedAt!.Value).TotalDays).ToList();
                var max = mapa.TryGetValue(g.Key, out var d) ? d : mapa[""];
                return new PrazoDaFamilia(g.Key, max > 0 ? max : null, diasDeCada.Count,
                    max > 0 ? diasDeCada.Count(x => Math.Floor(x) <= max) : 0, Media(diasDeCada));
            })
            .OrderByDescending(x => x.Measured).ThenBy(x => x.Family, StringComparer.Ordinal).Take(Top).ToList();

        // as escolhas do filtro saem do cadastro inteiro, não do recorte (RecorteDoPainel)
        var opcoes = await RecorteDoPainel.OpcoesAsync(db.MaterialRequisitions.AsNoTracking(), ct);
        var centros = opcoes.Centros
            .Select(c => new CentroDeMaterial(c, ccNome.TryGetValue(c, out var n) ? n : c)).ToList();
        var familias = await db.ProductFamilies.AsNoTracking()
            .Where(f => f.MaterialRequestable).OrderBy(f => f.Name).Select(f => f.Name).ToListAsync(ct);

        var kpis = new KpisDeMaterial(
            criadas.Count, criadasAntes,
            criadas.SelectMany(Itens).Sum(i => i.Quantity), atendidas.SelectMany(Itens).Sum(i => i.FulfilledQuantity),
            esperandoAprovacao.Count, naFila.Count, atendidas.Count, parciais, rejeitadas, canceladas,
            itensParaCompra, mediaAprovacao, mediaAtendimento, mediaTotal,
            noPrazo, medidas.Count, estouradasAbertas);

        return new RelatorioDeMaterial(from, to, kpis, meses, porCentro, porFamilia, porProduto, porSolicitante,
            prazoPorFamilia, new OpcoesDeMaterial(centros, familias), Definicoes);
    }

    private static DateTimeOffset MeiaNoite(DateOnly dia) =>
        new(dia.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
