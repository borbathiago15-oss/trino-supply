using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Analytics;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Materials;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// Uma linha do radar do almoxarifado. `Ordem` é a gravidade (menor = mais grave), como no
/// radar da compra: o prazo estourado assume o topo sozinho, sem ninguém reordenar.
/// </summary>
public record AlertaDoAlmoxarifado(
    string Id, string Tipo, string Numero, string Descricao, string CentroCusto, string Solicitante,
    string Tempo, int Ordem)
{
    public const string PrazoEstourado = "PRAZO_ESTOURADO";
    public const string PrazoEmAtencao = "PRAZO_ATENCAO";
    public const string AguardandoOCentro = "AGUARDANDO_CENTRO";
    public const string RotaDeCompra = "ROTA_DE_COMPRA";
}

/// <summary>Uma linha de ranking do mês, dita para quem lê de cinco metros.</summary>
public record RankingDoMaterial(string Rotulo, int Solicitacoes, decimal Quantidade);

/// <summary>
/// A segunda tela da parede: a solicitação de material, no rodízio com a tela da compra.
///
/// <para>
/// <b>Nenhum número nasce aqui.</b> O bloco do almoxarifado é o mesmo que a tela da compra já
/// mostra (<see cref="TorreDeControleService.AlmoxarifadoAsync"/>); o mês — solicitadas,
/// atendidas, atendidas no prazo, fora do prazo, rankings — é o do Dashboard
/// (<see cref="AnalyticsDeMaterialService.MaterialAsync"/>, do primeiro dia do mês até hoje);
/// e o prazo de cada linha do radar é o de <see cref="PrazoDeAtendimentoService"/>. Há teste
/// que compara a parede com o painel: a TV fica na sala onde o almoxarife trabalha, e se ela
/// dissesse "8 fora do prazo" com o painel dele dizendo 6, as duas perderiam a autoridade.
/// </para>
///
/// <para>
/// O recorte por <b>unidade</b> é o mesmo do bloco do almoxarifado: a solicitação não tem
/// empresa, então ela entra pela do centro de custo, e centro sem empresa cadastrada conta só
/// na visão geral. É por isso que o recorte do mês vai ao Dashboard como a <b>lista de
/// centros</b> da unidade, e não como um nome de empresa que a solicitação não carrega.
/// </para>
/// </summary>
public record CockpitDoMaterialResponse(
    DateTimeOffset SincronizadoEm,
    string? Unidade,
    IReadOnlyList<string> Unidades,
    /// <summary>O mesmo bloco da tela da compra: as duas filas, o atendido hoje e no mês.</summary>
    AlmoxarifadoDoCockpit Almoxarifado,
    /// <summary>A espera da fila do centro: a mais antiga aguardando o Nível 1, e a cor pela régua da esteira.</summary>
    int HorasDoMaisAntigoAguardando, string GargaloAguardando, string? MaisAntigaAguardandoNumero,
    /// <summary>Na fila do estoque hoje com o prazo de atendimento estourado — o `slaBreachedOpen` do painel.</summary>
    int ForaDoPrazo,
    /// <summary>Na fila do estoque hoje a 80% do prazo ou mais: avisar no dia do vencimento é avisar tarde.</summary>
    int EmAtencao,
    /// <summary>Atendidas no prazo no mês, das medidas — nulo sem atendimento medido, porque 0% diria "todas atrasaram".</summary>
    double? AtendidasNoPrazoPct, int AtendidasMedidas,
    int SolicitadasNoMes, int AtendidasNoMes, double? HorasMediaAprovacao,
    IReadOnlyList<AlertaDoAlmoxarifado> Radar,
    IReadOnlyList<RankingDoMaterial> PorCentro,
    IReadOnlyList<RankingDoMaterial> PorFamilia,
    IReadOnlyList<RankingDoMaterial> PorProduto);

public class CockpitDoMaterialService(
    AppDbContext db, TimeProvider clock, TorreDeControleService torre,
    AnalyticsDeMaterialService analytics, PrazoDeAtendimentoService prazos)
{
    public const int TetoDoRadar = 40;
    /// <summary>Cinco linhas por ranking: é o que cabe numa coluna da parede sem encolher a fonte.</summary>
    public const int TopoDoRanking = 5;

    public async Task<CockpitDoMaterialResponse> ObterAsync(string? unidade = null, CancellationToken ct = default)
    {
        var agora = clock.GetUtcNow();
        var hoje = DateOnly.FromDateTime(agora.UtcDateTime);
        unidade = string.IsNullOrWhiteSpace(unidade) ? null : unidade.Trim();

        var unidades = await torre.UnidadesAsync(ct);
        var almoxarifado = await torre.AlmoxarifadoAsync(unidade, hoje, agora, ct);

        // a unidade vira a lista de centros dela: é o único caminho da solicitação até a empresa
        string[]? centrosDaUnidade = null;
        Dictionary<string, string>? empresaPorCentro = null;
        if (unidade is not null)
        {
            empresaPorCentro = await torre.EmpresaPorCentroAsync(ct);
            centrosDaUnidade = [.. empresaPorCentro.Where(kv => kv.Value == unidade).Select(kv => kv.Key)];
        }

        var inicioDoMes = new DateOnly(hoje.Year, hoje.Month, 1);
        var mes = await analytics.MaterialAsync(inicioDoMes, hoje, null, null, null, centrosDaUnidade, ct: ct);

        // o radar: o que está aberto agora, no recorte, com o prazo de cada linha
        var abertas = await db.MaterialRequisitions.AsNoTracking().Include(r => r.Items)
            .Where(r => r.Status == MaterialRequisitionStatus.Submitted || r.Status == MaterialRequisitionStatus.Approved)
            .ToListAsync(ct);
        var inicioDoMesDt = new DateTimeOffset(inicioDoMes.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var viraramCompra = await db.MaterialRequisitions.AsNoTracking().Include(r => r.Items)
            .Where(r => r.FulfilledAt != null && r.FulfilledAt >= inicioDoMesDt && r.PurchaseRequisitionId != null)
            .ToListAsync(ct);
        if (centrosDaUnidade is not null)
        {
            bool DaUnidade(MaterialRequisition r) =>
                empresaPorCentro!.GetValueOrDefault(r.CostCenter.Trim().ToUpperInvariant()) == unidade;
            abertas = [.. abertas.Where(DaUnidade)];
            viraramCompra = [.. viraramCompra.Where(DaUnidade)];
        }
        var situacoes = await prazos.SituacoesAsync(abertas, ct);
        var ccNome = (await db.CostCenters.AsNoTracking().Select(c => new { c.Code, c.Name }).ToListAsync(ct))
            .GroupBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
        string Centro(MaterialRequisition r) => ccNome.TryGetValue(r.CostCenter, out var n) ? n : r.CostCenter;
        static string Descricao(MaterialRequisition r)
        {
            var primeiro = r.Items.FirstOrDefault();
            var texto = primeiro is null ? "—" : $"{primeiro.EffectiveQuantity:0.##}× {primeiro.Description}";
            return r.Items.Count > 1 ? $"{texto} (+{r.Items.Count - 1})" : texto;
        }

        var radar = new List<AlertaDoAlmoxarifado>();
        foreach (var r in abertas.Where(r => r.Status == MaterialRequisitionStatus.Approved))
        {
            var s = situacoes.GetValueOrDefault(r.Id);
            if (s is not { Status: "ESTOURADO" or "ATENCAO" }) continue;
            var estourou = s.Status == "ESTOURADO";
            radar.Add(new(r.Id.ToString(),
                estourou ? AlertaDoAlmoxarifado.PrazoEstourado : AlertaDoAlmoxarifado.PrazoEmAtencao,
                r.Number, Descricao(r), Centro(r), r.RequesterLabel,
                $"{s.Days}d na fila · prazo {s.MaxDays}d ({s.Family})", estourou ? 0 : 1));
        }
        // a fila do centro não tem prazo cadastrado; o que acende é a mesma régua de gargalo da esteira
        var aguardando = abertas.Where(r => r.Status == MaterialRequisitionStatus.Submitted)
            .OrderBy(r => r.CreatedAt).ToList();
        var horasAguardando = aguardando.Count == 0 ? 0 : (int)Math.Max(0, (agora - aguardando[0].CreatedAt).TotalHours);
        foreach (var r in aguardando)
        {
            var horas = (int)Math.Max(0, (agora - r.CreatedAt).TotalHours);
            if (horas <= TorreDeControleService.GargaloAtencaoHoras) continue;
            radar.Add(new(r.Id.ToString(), AlertaDoAlmoxarifado.AguardandoOCentro, r.Number, Descricao(r), Centro(r),
                r.RequesterLabel, $"{horas / 24}d aguardando o Nível 1", 2));
        }
        foreach (var r in viraramCompra.OrderByDescending(r => r.FulfilledAt))
            radar.Add(new(r.Id.ToString(), AlertaDoAlmoxarifado.RotaDeCompra, r.Number, Descricao(r), Centro(r),
                r.RequesterLabel, $"virou {r.PurchaseRequisitionNumber ?? "compra"}", 3));

        static IReadOnlyList<RankingDoMaterial> Topo(IEnumerable<LinhaDeMaterial> linhas) =>
            [.. linhas.Take(TopoDoRanking).Select(l => new RankingDoMaterial(l.Label, l.Count, l.Qty))];

        return new CockpitDoMaterialResponse(
            agora, unidade, unidades, almoxarifado,
            horasAguardando, TorreDeControleService.GargaloDe(aguardando.Count, horasAguardando),
            aguardando.FirstOrDefault()?.Number,
            ForaDoPrazo: mes.Kpis.SlaBreachedOpen,
            EmAtencao: abertas.Count(r => r.Status == MaterialRequisitionStatus.Approved
                                          && situacoes.GetValueOrDefault(r.Id)?.Status == "ATENCAO"),
            AtendidasNoPrazoPct: mes.Kpis.SlaMetPercent,
            AtendidasMedidas: mes.Kpis.SlaMeasured,
            SolicitadasNoMes: mes.Kpis.Requested,
            AtendidasNoMes: mes.Kpis.Fulfilled,
            HorasMediaAprovacao: mes.Kpis.AvgApprovalHours,
            // gravidade primeiro, depois quem espera há mais tempo: o estourado que acabou de
            // entrar sobe ao topo sem ninguém reordenar — como no radar da compra
            [.. radar.OrderBy(x => x.Ordem).ThenBy(x => x.Numero, StringComparer.Ordinal).Take(TetoDoRadar)],
            Topo(mes.ByCostCenter), Topo(mes.ByFamily), Topo(mes.ByProduct));
    }
}
