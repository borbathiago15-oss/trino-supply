using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// O cockpit da TV da sala de suprimentos. Ele é uma <b>vista</b> da Torre, não uma segunda
/// derivação: etapa, atraso, exceção e prazo saem das mesmas funções que a tela do comprador
/// usa (<see cref="ProcessStatus.Of"/>, <see cref="EtapaDe"/>, <see cref="ExcecaoDe"/>,
/// <see cref="PrazoDaEtapaService.Avaliar"/>).
///
/// <para>
/// Isso não é preciosismo de arquitetura: a TV fica ligada na sala onde o comprador trabalha.
/// Se o painel da parede dissesse "8 atrasados" e a Torre dele dissesse 6, as duas telas
/// perderiam a autoridade no mesmo instante, e ninguém saberia qual seguir.
/// </para>
///
/// <para>
/// A conta é uma passada só sobre as solicitações abertas, como <c>KpisAsync</c> já faz. O
/// pedido original falava em agregação SQL pura, e ela não serve aqui: etapa, exceção e
/// prazo <b>não existem em coluna</b> — são derivados de solicitação, cotação e pedido. Uma
/// agregação em SQL teria de reimplementar essas regras em outro lugar, e seria a segunda
/// fonte de verdade que a Torre existe para não ter.
/// </para>
/// </summary>
public partial class TorreDeControleService
{
    /// <summary>Meta institucional de itens dentro do prazo da etapa.</summary>
    public const decimal MetaSlaSemanal = 90m;

    /// <summary>
    /// Ponto de partida da meta mensal de saving, como <c>PrazoDaEtapaService.Padrao</c> é o
    /// das etapas: vale enquanto ninguém configurar a da empresa. Número na parede sem dono
    /// vira número que ninguém persegue — está aqui, num lugar só, para ser trocado.
    /// </summary>
    public const decimal MetaSavingMensal = 50_000m;

    private const int GargaloAtencaoHoras = 48;
    private const int GargaloCriticoHoras = 72;

    /// <summary>Quantas linhas o radar entrega. A TV mostra as mais graves; o resto é a Torre.</summary>
    public const int TetoDoRadar = 40;

    /// <param name="unidade">
    /// A empresa do recorte. Nulo é a visão geral — é ela que a TV mostra entre uma unidade e
    /// outra, e é o padrão de quem abre a tela sem escolher nada.
    /// </param>
    public async Task<CockpitResponse> CockpitAsync(string? unidade = null, CancellationToken ct = default)
    {
        var agora = clock.GetUtcNow();
        var hoje = DateOnly.FromDateTime(agora.UtcDateTime);
        unidade = string.IsNullOrWhiteSpace(unidade) ? null : unidade.Trim();

        // as unidades saem do recorte inteiro, e não do filtrado: a lista que a TV usa para
        // girar não pode encolher a cada volta até sobrar só a unidade que está na tela
        var unidades = await db.Requisitions
            .Where(r => r.DeletedAt == null && r.Status != RequisitionStatus.Draft && r.Company != null)
            .Select(r => r.Company!).Distinct().OrderBy(c => c).ToListAsync(ct);

        // mesmo recorte de KpisAsync: o que está aberto, do mais novo para o mais velho
        var consulta = db.Requisitions.Include(r => r.Items)
            .Where(r => r.DeletedAt == null && r.Status != RequisitionStatus.Draft);
        if (unidade is not null) consulta = consulta.Where(r => r.Company == unidade);
        var abertas = await consulta
            .OrderByDescending(r => r.CreatedAt).Take(2000).ToListAsync(ct);
        var ids = abertas.Select(r => r.Id).ToList();

        var prazosPorTipo = await new PrazoDaEtapaService(db, clock).MapaPorTipoAsync(ct);
        var cotacoes = await db.Quotations.Include(q => q.Items)
            .Include(q => q.Suppliers).Include(q => q.Proposals)
            .Where(q => ids.Contains(q.SourcePrId)
                        || q.Items.Any(i => i.SourcePrId != null && ids.Contains(i.SourcePrId.Value)))
            .OrderByDescending(q => q.CreatedAt).ToListAsync(ct);
        var cotacaoIds = cotacoes.Select(q => q.Id).ToList();
        var pedidos = await db.PurchaseOrders.Include(o => o.Items).Include(o => o.Invoices)
            .Where(o => (o.SourcePrId != null && ids.Contains(o.SourcePrId.Value))
                        || (o.QuotationId != null && cotacaoIds.Contains(o.QuotationId.Value)))
            .OrderByDescending(o => o.CreatedAt).ToListAsync(ct);

        var cotacaoPorSc = new Dictionary<Guid, Quotation>();
        foreach (var q in cotacoes)
            foreach (var id in ids.Where(q.CoversPr)) cotacaoPorSc.TryAdd(id, q);
        var pedidoPorSc = new Dictionary<Guid, PurchaseOrder>();
        var pedidoPorCotacao = new Dictionary<Guid, PurchaseOrder>();
        foreach (var o in pedidos)
        {
            if (o.SourcePrId is { } pid) pedidoPorSc.TryAdd(pid, o);
            if (o.QuotationId is { } qid) pedidoPorCotacao.TryAdd(qid, o);
        }

        var porEtapa = new Dictionary<string, (int Itens, int HorasDoMaisAntigo)>();
        var radar = new List<ExcecaoDoCockpit>();
        var burndown = new Dictionary<string, (int Atendidos, int Total, int Criticas)>();
        int atrasados = 0, emRisco = 0, backlogItens = 0, dentroDoPrazo = 0, medidosNoPrazo = 0;
        decimal backlogValor = 0;
        var semanaAtras = agora.AddDays(-7);

        foreach (var sc in abertas)
        {
            cotacaoPorSc.TryGetValue(sc.Id, out var cotacao);
            PurchaseOrder? pedido = null;
            if (!pedidoPorSc.TryGetValue(sc.Id, out pedido) && cotacao is not null)
                pedidoPorCotacao.TryGetValue(cotacao.Id, out pedido);

            var situacao = ProcessStatus.Of(sc, cotacao, pedido);
            var etapa = EtapaDe(situacao.Key);
            var encerrado = etapa == "ENCERRADO";
            var previsao = pedido?.PromisedDate ?? sc.NeededBy;
            var atrasada = !encerrado && previsao is not null && previsao < hoje;
            var motivoDaExcecao = ExcecaoDe(pedido);
            var espera = EsperaDe(sc, cotacao, pedido, AlcadasDoCentro.Nenhuma, hoje, agora);
            var estourou = PrazoDaEtapaService.Avaliar(etapa, DiasQueOPrazoCobra(situacao.Key, espera),
                prazosPorTipo.GetValueOrDefault(
                    TipoDeSolicitacaoService.Normalizar(sc.NeedType), prazosPorTipo[""])).Breached;
            var desde = sc.DecidedAt ?? sc.SubmittedAt;
            var horasNaFila = desde is { } d ? (int)Math.Max(0, (agora - d).TotalHours) : 0;
            // a mesma régua da coluna da Torre: quem conduziu a cotação, e não o aprovador
            var comprador = CompradorDe(sc, cotacao).Label is { Length: > 0 } nome ? nome : "Sem responsável";

            if (!encerrado)
            {
                var atual = porEtapa.GetValueOrDefault(etapa);
                porEtapa[etapa] = (atual.Itens + sc.Items.Count,
                    Math.Max(atual.HorasDoMaisAntigo, horasNaFila));
            }

            // burndown do dia: o que este comprador fechou hoje contra o que está na mão dele
            var doDia = burndown.GetValueOrDefault(comprador);
            var fechouHoje = pedido?.CreatedAt is { } criadoEm
                             && DateOnly.FromDateTime(criadoEm.UtcDateTime) == hoje;
            burndown[comprador] = (
                doDia.Atendidos + (fechouHoje ? sc.Items.Count : 0),
                doDia.Total + (encerrado && !fechouHoje ? 0 : sc.Items.Count),
                doDia.Criticas + (!encerrado && (atrasada || estourou) ? sc.Items.Count : 0));

            foreach (var _ in sc.Items)
            {
                if (encerrado) continue;
                backlogItens++;
                backlogValor += ValorDoItem(sc, pedido);
                if (atrasada) atrasados++;
                // risco é a união, não a soma: o item atrasado E com prazo de etapa estourado
                // é um só — somar os dois contadores daria mais itens em risco que no backlog
                if (atrasada || estourou) emRisco++;
                if (espera?.Since >= semanaAtras)
                {
                    medidosNoPrazo++;
                    if (!estourou) dentroDoPrazo++;
                }
            }

            if (!encerrado) radar.AddRange(AlertasDe(sc, cotacao, pedido, previsao, hoje, agora, comprador));
        }

        var esteira = Etapas
            .Where(e => e.Key != "ENCERRADO")
            .Select(e =>
            {
                var (itens, horas) = porEtapa.GetValueOrDefault(e.Key);
                var gargalo = itens == 0 ? NoDaEsteira.Normal
                    : horas > GargaloCriticoHoras ? NoDaEsteira.Critico
                    : horas > GargaloAtencaoHoras ? NoDaEsteira.Atencao
                    : NoDaEsteira.Normal;
                return new NoDaEsteira(e.Key, e.Label, itens, horas, gargalo);
            }).ToList();

        // as duas contas que não saem da passada acima, cada uma no seu recorte de tempo
        var saving = await SavingDoMesAsync(agora, ct);
        var otif = await OtifRecenteAsync(agora, ct);

        return new CockpitResponse(
            agora,
            unidade,
            unidades,
            new CockpitKpis(
                ItensAtrasados: atrasados,
                TaxaRiscoPct: Pct(emRisco, backlogItens) ?? 0,
                BacklogTotalItens: backlogItens,
                BacklogTotalValor: decimal.Round(backlogValor, 2),
                SlaSemanalPct: Pct(dentroDoPrazo, medidosNoPrazo) ?? 100,
                MetaSlaPct: MetaSlaSemanal,
                SavingMesTotal: saving,
                MetaSavingMes: MetaSavingMensal,
                OtifGeralPct: otif.Pct,
                OtifMedidos: otif.Medidos),
            esteira,
            // gravidade primeiro, espera depois: o urgente que acabou de entrar sobe ao topo
            // sem ninguém reordenar nada — é o critério de aceite 3
            [.. radar.OrderBy(x => x.Ordem).ThenByDescending(x => x.TempoRestanteOuAtraso.Length).Take(TetoDoRadar)],
            [.. burndown.Where(b => b.Value.Total > 0)
                .OrderByDescending(b => b.Value.Criticas).ThenByDescending(b => b.Value.Total)
                .Select(b => new BurndownDoComprador(b.Key, b.Value.Atendidos, b.Value.Total, b.Value.Criticas))],
            await AgendaDaDocaAsync(hoje, ct));
    }

    private static decimal ValorDoItem(PurchaseRequisition sc, PurchaseOrder? pedido)
    {
        if (pedido is not null && pedido.Items.Count > 0)
            return pedido.Items.Sum(i => (i.UnitPrice ?? 0) * i.Quantity) / Math.Max(1, sc.Items.Count);
        return sc.Items.Sum(i => (i.EstimatedUnitPrice ?? 0) * i.Quantity) / Math.Max(1, sc.Items.Count);
    }

    private static decimal? Pct(int parte, int todo) =>
        todo == 0 ? null : decimal.Round(parte * 100m / todo, 1);

    /// <summary>
    /// As três famílias de alerta do radar, na ordem de gravidade. Uma SC pode render mais de
    /// um alerta — atrasada e sem O.C. ao mesmo tempo é exatamente o caso que merece os dois.
    /// </summary>
    private static IEnumerable<ExcecaoDoCockpit> AlertasDe(
        PurchaseRequisition sc, Quotation? cotacao, PurchaseOrder? pedido,
        DateOnly? previsao, DateOnly hoje, DateTimeOffset agora, string comprador)
    {
        var centro = string.IsNullOrWhiteSpace(sc.CostCenter) ? "—" : sc.CostCenter;
        var descricao = sc.Items.FirstOrDefault()?.Description ?? sc.Justification;
        if (sc.Items.Count > 1) descricao += $" (+{sc.Items.Count - 1})";

        // vermelho: a data de necessidade estourou
        if (previsao is { } data && data < hoje)
        {
            var dias = hoje.DayNumber - data.DayNumber;
            yield return new(sc.Id.ToString(), ExcecaoDoCockpit.AtrasoCritico, sc.Number, descricao,
                centro, $"{dias}d de atraso", comprador, 0);
        }

        // Amarelo: o convite está no último dia, ou só um fornecedor respondeu.
        //
        // O pedido falava em "vencendo nas próximas 4 horas". O prazo do convite é `DateOnly`
        // (`QuotationSupplier.PrazoEfetivo`), então hora não existe nesse dado: inventar uma
        // daria um relógio que conta para um instante que o sistema nunca gravou. "Vence hoje"
        // é a mesma urgência, dita pelo que o dado sabe.
        if (cotacao is not null && cotacao.Status == QuotationStatus.Open)
        {
            var semResposta = cotacao.Suppliers
                .Where(s => !cotacao.Proposals.Any(p => p.SupplierId == s.SupplierId)).ToList();
            var vencendo = semResposta
                .Select(s => s.PrazoEfetivo(cotacao.Deadline))
                .Where(p => p is not null && p.Value <= hoje).ToList();
            if (vencendo.Count > 0)
                yield return new(cotacao.Id.ToString(), ExcecaoDoCockpit.CotacaoVencendo,
                    cotacao.Number, descricao, centro,
                    vencendo.Any(p => p!.Value < hoje)
                        ? $"prazo vencido — {vencendo.Count} sem resposta"
                        : $"vence hoje — {vencendo.Count} sem resposta",
                    comprador, 1);

            var responderam = cotacao.Proposals.Select(p => p.SupplierId).Distinct().Count();
            if (responderam == 1 && cotacao.Suppliers.Count > 1)
                yield return new(cotacao.Id.ToString(), ExcecaoDoCockpit.PropostaUnica,
                    cotacao.Number, descricao, centro,
                    $"1 de {cotacao.Suppliers.Count} responderam", comprador, 1);
        }

        // azul: aprovado há mais de 24h e ainda sem a O.C. do ERP
        if (cotacao?.DirectorApprovedAt is { } aprovadoEm
            && pedido is not null && pedido.ErpNumber is null && pedido.NoErpReason is null
            && agora - aprovadoEm > TimeSpan.FromHours(24))
        {
            var horas = (int)(agora - aprovadoEm).TotalHours;
            yield return new(pedido.Id.ToString(), ExcecaoDoCockpit.OcPendente, pedido.Number,
                descricao, centro, $"{horas}h sem O.C.", comprador, 2);
        }
    }

    /// <summary>Saving fechado no mês corrente, pelos processos aprovados dentro dele.</summary>
    private async Task<decimal> SavingDoMesAsync(DateTimeOffset agora, CancellationToken ct)
    {
        var inicio = new DateTimeOffset(new DateTime(agora.Year, agora.Month, 1), TimeSpan.Zero);
        // filtro na entidade, projeção por último: `SavingValue` é coluna e traduz
        return await db.Quotations
            .Where(q => q.SavingValue != null && q.SavingValue > 0
                        && q.DirectorApprovedAt != null && q.DirectorApprovedAt >= inicio)
            .SumAsync(q => q.SavingValue!.Value, ct);
    }

    /// <summary>
    /// OTIF dos últimos 30 dias. `Otif` é propriedade calculada e não existe em coluna, então
    /// a conta é em memória sobre o recorte — filtrar por ela em SQL não traduziria.
    /// </summary>
    private async Task<(decimal? Pct, int Medidos)> OtifRecenteAsync(DateTimeOffset agora, CancellationToken ct)
    {
        var desde = agora.AddDays(-30);
        var entregues = await db.PurchaseOrders.Include(o => o.Items)
            .Where(o => o.DeliveryCompletedAt != null && o.DeliveryCompletedAt >= desde)
            .ToListAsync(ct);
        var medidos = entregues.Where(o => o.Otif is not null).ToList();
        return (Pct(medidos.Count(o => o.Otif == true), medidos.Count), medidos.Count);
    }

    /// <summary>
    /// A doca de hoje: as notas lançadas cujo pedido ainda não teve a entrega concluída.
    /// "Descarregando" é o pedido que já recebeu parte — o material está na doca agora.
    /// </summary>
    private async Task<List<DescargaDoDia>> AgendaDaDocaAsync(DateOnly hoje, CancellationToken ct)
    {
        var pedidos = await db.PurchaseOrders.Include(o => o.Invoices).Include(o => o.Items)
            .Where(o => o.DeliveryCompletedAt == null
                        && o.Status != PurchaseOrderStatus.Cancelled
                        && o.Invoices.Any())
            .OrderBy(o => o.PromisedDate).Take(40).ToListAsync(ct);

        return [.. pedidos.SelectMany(o => o.Invoices.Select(nf => new DescargaDoDia(
            nf.Number,
            o.SupplierName,
            (o.PromisedDate ?? nf.IssuedOn).ToString("dd/MM"),
            o.Status == PurchaseOrderStatus.PartiallyReceived ? DescargaDoDia.Descarregando
                : o.PromisedDate is { } p && p < hoje ? DescargaDoDia.Atrasado
                : DescargaDoDia.NoPrazo)))
            .Take(20)];
    }
}
