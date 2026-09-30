using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Analytics;

/// <summary>
/// Um número do cockpit com o de antes ao lado. A comparação não é enfeite: numa TV,
/// "Backlog: 8" não diz nada sozinho — "8, e ontem eram 11" diz que o time está ganhando.
/// <para><c>Anterior</c> nulo quer dizer que não há com o que comparar (primeiro mês de
/// operação, série ainda curta); a tela então mostra só o número, em vez de inventar 0%.</para>
/// </summary>
public record NumeroDoCockpit(decimal Valor, decimal? Anterior);

/// <summary>
/// A Tela 1 — o resultado de Suprimentos, que é o que fica na TV para quem passa no
/// corredor. Todo número aqui tem origem única e datada; nenhum é estimado.
/// </summary>
public record BlocoExecutivo(
    /// <summary>Soma das O.C.s criadas no mês, contra o mesmo intervalo do mês anterior.</summary>
    NumeroDoCockpit ValorComprado,
    /// <summary>Saving de negociação das adjudicações do mês (<c>SavingValue</c>).</summary>
    NumeroDoCockpit Economia,
    /// <summary>Percentual da economia sobre o valor comprado; nulo quando não se comprou nada.</summary>
    decimal? EconomiaPercentual,
    /// <summary>
    /// Custo evitado dos pleitos de reajuste do mês. Fica **separado** da economia de
    /// propósito: são metodologias diferentes e somá-las daria um número que não existe.
    /// </summary>
    NumeroDoCockpit CustosEvitados,
    /// <summary>Entregas no prazo entre as concluídas no mês — nulo quando nenhuma concluiu.</summary>
    decimal? EntregasNoPrazoPercent, int EntregasNoPrazo, int EntregasConcluidas,
    /// <summary>Itens dentro do prazo da própria etapa, sobre o total em andamento.</summary>
    decimal? SlaDeComprasPercent, int PrazoEstourado, int EmAndamento,
    /// <summary>O que espera ação do comprador — a mesma conta do card da Torre.</summary>
    int Backlog);

/// <summary>Uma etapa do fluxo, com quanto há nela e quanto passou do prazo.</summary>
public record EtapaDoFluxo(string Key, string Label, int Quantidade, int AcimaDoPrazo);

/// <summary>
/// A Tela 2 — a operação de compras. Responde a pergunta que o documento chama de mais
/// importante: <b>o time está conseguindo processar a demanda que entra?</b>
/// </summary>
public record BlocoProdutividade(
    /// <summary>Itens de solicitação criados hoje.</summary>
    int EntraramHoje,
    /// <summary>Itens cuja entrega foi concluída hoje. É o outro extremo do mesmo cano.</summary>
    int ConcluidosHoje,
    /// <summary>
    /// Positivo é backlog crescendo; negativo, encolhendo. É <c>EntraramHoje − ConcluidosHoje</c>,
    /// e o sinal é a informação — o número absoluto sozinho não diz para que lado se anda.
    /// </summary>
    int Saldo,
    /// <summary>Concluídos sobre entrados; nulo quando não entrou nada e a divisão não existe.</summary>
    decimal? TaxaDeConclusao,
    int Backlog, int EmCotacao, int AguardandoAprovacao, int AguardandoOc, int Urgentes,
    IReadOnlyList<EtapaDoFluxo> Fluxo,
    /// <summary>
    /// A etapa com mais itens acima do prazo — nula quando nenhuma tem. O documento pede
    /// "onde precisamos atuar", e uma etapa apontada vale mais que seis números iguais.
    /// </summary>
    EtapaDoFluxo? Gargalo);

/// <summary>Uma linha da faixa vermelha. `Tone` é a urgência: alta, media, baixa.</summary>
public record AlertaDoCockpit(string Code, string Label, int Count, string Tone);

/// <summary>O que a TV mostra numa leitura.</summary>
public record Cockpit(
    DateTimeOffset At, BlocoExecutivo Executivo, BlocoProdutividade Produtividade,
    IReadOnlyList<AlertaDoCockpit> Alertas);

/// <summary>
/// O cockpit: a mesma verdade da Torre, no formato de quem olha de longe.
///
/// <para>
/// Ele <b>não recalcula nada</b>. Etapa, atraso, prazo estourado e o que precisa do
/// comprador saem de <see cref="TorreDeControleService.ConsultarAsync"/>, com a página
/// em 1 porque só os KPIs interessam — os KPIs são do recorte inteiro, não da página.
/// Fosse uma segunda conta, a TV e a Torre discordariam no primeiro caso de canto, e um
/// número numa parede que contradiz a tela do comprador não é informação: é ruído que
/// destrói a confiança nos dois.
/// </para>
///
/// <para>
/// O que ele acrescenta é o <b>dinheiro</b> (comprado, economia, custo evitado) e a
/// <b>vazão do dia</b> (entrou × concluiu), que a Torre não responde porque não é a
/// pergunta dela.
/// </para>
/// </summary>
public class CockpitService(AppDbContext db, TorreDeControleService torre, TimeProvider clock)
{
    /// <summary>Quem vê a TV vê números do setor inteiro — o mesmo recorte da Torre.</summary>
    public static bool CanView(string role) => TorreDeControleService.CanView(role);

    public async Task<Cockpit> LerAsync(CancellationToken ct = default)
    {
        var agora = clock.GetUtcNow();
        var hoje = DateOnly.FromDateTime(agora.UtcDateTime);
        var inicioDoMes = new DateOnly(hoje.Year, hoje.Month, 1);
        var inicioAnterior = inicioDoMes.AddMonths(-1);

        var pagina = await torre.ConsultarAsync(new FiltroTorre(PageSize: 1), ct);
        var kpis = pagina.Kpis;

        var executivo = await ExecutivoAsync(inicioDoMes, inicioAnterior, hoje, kpis, ct);
        var produtividade = await ProdutividadeAsync(hoje, kpis, ct);

        return new Cockpit(agora, executivo, produtividade, Alertas(kpis));
    }

    private async Task<BlocoExecutivo> ExecutivoAsync(
        DateOnly inicioDoMes, DateOnly inicioAnterior, DateOnly hoje,
        KpisDaTorre kpis, CancellationToken ct)
    {
        var deste = Instante(inicioDoMes);
        var doAnterior = Instante(inicioAnterior);

        // as duas janelas na mesma consulta: são a mesma tabela e o mesmo recorte, e
        // duas idas ao banco para isso pagariam o dobro a cada giro da TV
        var pedidos = await db.PurchaseOrders
            .Where(o => o.CreatedAt >= doAnterior && o.Status != PurchaseOrderStatus.Cancelled)
            .Select(o => new { o.CreatedAt, o.TotalValue, o.DeliveryCompletedAt, o.PromisedDate })
            .ToListAsync(ct);

        var comprado = pedidos.Where(o => o.CreatedAt >= deste).Sum(o => o.TotalValue);
        var compradoAntes = pedidos.Where(o => o.CreatedAt < deste).Sum(o => o.TotalValue);

        // a economia é do **processo**, não da adjudicação: o saving de negociação se mede
        // contra a primeira proposta do vencedor, e essa comparação é uma por cotação. A
        // data é a da escolha, que é quando o ganho passou a existir
        var processos = await db.Quotations
            .Where(q => q.SelectedAt != null && q.SelectedAt >= doAnterior && q.SavingValue != null)
            .Select(q => new { Em = q.SelectedAt!.Value, Saving = q.SavingValue!.Value })
            .ToListAsync(ct);
        var economia = processos.Where(a => a.Em >= deste).Sum(a => a.Saving);
        var economiaAntes = processos.Where(a => a.Em < deste).Sum(a => a.Saving);

        var pleitos = await db.ContractAdjustments
            .Where(a => a.CreatedAt >= doAnterior)
            .Select(a => new { a.CreatedAt, a.CostAvoidance })
            .ToListAsync(ct);
        var evitado = pleitos.Where(a => a.CreatedAt >= deste).Sum(a => a.CostAvoidance);
        var evitadoAntes = pleitos.Where(a => a.CreatedAt < deste).Sum(a => a.CostAvoidance);

        // OTIF do mês: só as entregas concluídas contam. Pedido ainda em trânsito não é
        // atraso nem acerto — contá-lo como qualquer um dos dois falsearia o percentual
        var concluidas = pedidos
            .Where(o => o.DeliveryCompletedAt is not null && o.DeliveryCompletedAt >= deste).ToList();
        var noPrazo = concluidas.Count(o => o.PromisedDate is null
            || DateOnly.FromDateTime(o.DeliveryCompletedAt!.Value.UtcDateTime) <= o.PromisedDate);

        return new BlocoExecutivo(
            new NumeroDoCockpit(comprado, compradoAntes),
            new NumeroDoCockpit(economia, economiaAntes),
            comprado > 0 ? Math.Round(economia / comprado * 100m, 1) : null,
            new NumeroDoCockpit(evitado, evitadoAntes),
            concluidas.Count > 0 ? Math.Round((decimal)noPrazo / concluidas.Count * 100m, 1) : null,
            noPrazo, concluidas.Count,
            kpis.Total > 0 ? Math.Round((decimal)(kpis.Total - kpis.PrazoEstourado) / kpis.Total * 100m, 1) : null,
            kpis.PrazoEstourado, kpis.Total,
            kpis.PrecisaDeVoce);
    }

    private async Task<BlocoProdutividade> ProdutividadeAsync(
        DateOnly hoje, KpisDaTorre kpis, CancellationToken ct)
    {
        var deHoje = Instante(hoje);

        // a unidade é o item, como na Torre: uma SC de cinco itens é cinco demandas, e
        // contá-la como uma esconderia quatro quintos do trabalho que entrou
        var entraram = await db.Requisitions
            .Where(r => r.CreatedAt >= deHoje)
            .SelectMany(r => r.Items).CountAsync(ct);

        var concluidos = await db.PurchaseOrders
            .Where(o => o.DeliveryCompletedAt != null && o.DeliveryCompletedAt >= deHoje)
            .SelectMany(o => o.Items).CountAsync(ct);

        var fluxo = TorreDeControleService.Etapas
            .Where(e => e.Key != "ENCERRADO")
            .Select(e => new EtapaDoFluxo(e.Key, e.Label, QuantidadeNaEtapa(e.Key, kpis), 0))
            .ToList();

        // o gargalo é a etapa mais cheia entre as que têm alguém esperando. O prazo
        // estourado é do processo inteiro (a Torre o conta uma vez), então a etapa não
        // carrega o seu próprio — apontar a mais cheia é o que o dado de hoje sustenta
        var gargalo = fluxo.Where(e => e.Quantidade > 0)
            .OrderByDescending(e => e.Quantidade).FirstOrDefault();

        return new BlocoProdutividade(
            entraram, concluidos, entraram - concluidos,
            entraram > 0 ? Math.Round((decimal)concluidos / entraram * 100m, 1) : null,
            kpis.PrecisaDeVoce, kpis.EmCotacao, kpis.AguardandoAprovacao, kpis.AguardandoOc,
            kpis.Urgentes, fluxo, gargalo);
    }

    private static int QuantidadeNaEtapa(string key, KpisDaTorre k) => key switch
    {
        "SOLICITACAO" => k.Novos,
        "COTACAO" => k.EmCotacao,
        "APROVACAO" => k.AguardandoAprovacao,
        "ORDEM_DE_COMPRA" => k.AguardandoOc,
        "RECEBIMENTO" => k.EmFaturamento + k.AguardandoRecebimento,
        _ => 0,
    };

    /// <summary>
    /// A faixa vermelha do rodapé. Entra só o que **alguém tem de fazer hoje** — alarme que
    /// grita sempre para de ser lido, então contagem zerada não vira linha.
    /// </summary>
    private static IReadOnlyList<AlertaDoCockpit> Alertas(KpisDaTorre k) => new[]
    {
        new AlertaDoCockpit("ATRASADOS", "atrasados", k.Atrasados, "alta"),
        new AlertaDoCockpit("PRAZO_ESTOURADO", "com prazo de etapa estourado", k.PrazoEstourado, "alta"),
        new AlertaDoCockpit("EXCECOES", "em exceção", k.Excecoes, "alta"),
        new AlertaDoCockpit("SEM_OC", "aguardando O.C.", k.AguardandoOc, "media"),
        new AlertaDoCockpit("URGENTES", "urgentes", k.Urgentes, "media"),
        new AlertaDoCockpit("APROVACAO", "aguardando aprovação", k.AguardandoAprovacao, "baixa"),
    }.Where(a => a.Count > 0).ToList();

    private static DateTimeOffset Instante(DateOnly d) =>
        new(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
