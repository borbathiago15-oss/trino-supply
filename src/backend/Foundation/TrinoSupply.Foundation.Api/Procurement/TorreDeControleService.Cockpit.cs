using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Analytics;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Materials;

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
    /// das etapas: vale enquanto ninguém configurar a da empresa. A meta cadastrada em
    /// Metas dos indicadores (<c>saving</c>) vence este número — a parede e o painel cobram a mesma.
    /// </summary>
    public const decimal MetaSavingMensal = 50_000m;

    internal const int GargaloAtencaoHoras = 48;
    internal const int GargaloCriticoHoras = 72;

    /// <summary>A régua de cor da esteira, dita uma vez: a parede do material usa a mesma.</summary>
    internal static string GargaloDe(int quantidade, int horasDoMaisAntigo) =>
        quantidade == 0 ? NoDaEsteira.Normal
        : horasDoMaisAntigo > GargaloCriticoHoras ? NoDaEsteira.Critico
        : horasDoMaisAntigo > GargaloAtencaoHoras ? NoDaEsteira.Atencao
        : NoDaEsteira.Normal;

    /// <summary>
    /// As unidades que a TV percorre. Saem do recorte inteiro, e não do filtrado: a lista que a
    /// parede usa para girar não pode encolher a cada volta até sobrar só a unidade da tela.
    /// </summary>
    internal Task<List<string>> UnidadesAsync(CancellationToken ct) =>
        db.Requisitions
            .Where(r => r.DeletedAt == null && r.Status != RequisitionStatus.Draft && r.Company != null)
            .Select(r => r.Company!).Distinct().OrderBy(c => c).ToListAsync(ct);

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

        var unidades = await UnidadesAsync(ct);

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

        // a mesma resolução por item da Torre: a parede e a mesa perguntam à mesma classe
        var andamento = new AndamentoDosItens(cotacoes, pedidos);

        var porEtapa = new Dictionary<string, (int Itens, int HorasDoMaisAntigo)>();
        var radar = new List<ExcecaoDoCockpit>();
        var burndown = new Dictionary<string, (int Atendidos, int Total, int Criticas)>();
        int atrasados = 0, emRisco = 0, backlogItens = 0, dentroDoPrazo = 0, medidosNoPrazo = 0;
        int entraramHoje = 0, concluidosHoje = 0;
        decimal backlogValor = 0;
        var semanaAtras = agora.AddDays(-7);

        foreach (var sc in abertas)
        foreach (var grupo in sc.Items.GroupBy(i => andamento.Do(sc, i)))
        {
            var (cotacao, pedido) = grupo.Key;
            var itensDoGrupo = grupo.Count();

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

            // a vazão do dia, nos dois extremos do cano. Entra pela criação da SC e sai pela
            // entrega concluída: as duas são datas que o sistema grava, e nenhuma é estimada.
            // Fica fora do `if (!encerrado)` de propósito — o que concluiu hoje É encerrado,
            // e contá-lo só enquanto está aberto zeraria justamente o lado da capacidade
            if (DateOnly.FromDateTime(sc.CreatedAt.UtcDateTime) == hoje) entraramHoje += itensDoGrupo;
            if (pedido?.DeliveryCompletedAt is { } entregueEm
                && DateOnly.FromDateTime(entregueEm.UtcDateTime) == hoje) concluidosHoje += itensDoGrupo;

            if (!encerrado)
            {
                var atual = porEtapa.GetValueOrDefault(etapa);
                porEtapa[etapa] = (atual.Itens + itensDoGrupo,
                    Math.Max(atual.HorasDoMaisAntigo, horasNaFila));
            }

            // burndown do dia: o que este comprador fechou hoje contra o que está na mão dele
            var doDia = burndown.GetValueOrDefault(comprador);
            var fechouHoje = pedido?.CreatedAt is { } criadoEm
                             && DateOnly.FromDateTime(criadoEm.UtcDateTime) == hoje;
            burndown[comprador] = (
                doDia.Atendidos + (fechouHoje ? itensDoGrupo : 0),
                doDia.Total + (encerrado && !fechouHoje ? 0 : itensDoGrupo),
                doDia.Criticas + (!encerrado && (atrasada || estourou) ? itensDoGrupo : 0));

            foreach (var _ in grupo)
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
                MetaSavingMes: await db.IndicatorGoals.Where(g => g.Indicator == "saving")
                    .Select(g => (decimal?)g.MonthlyValue).FirstOrDefaultAsync(ct) ?? MetaSavingMensal,
                OtifGeralPct: otif.Pct,
                OtifMedidos: otif.Medidos),
            new VazaoDoDia(entraramHoje, concluidosHoje, entraramHoje - concluidosHoje,
                Pct(concluidosHoje, entraramHoje)),
            await CompraDoMesAsync(unidade, agora, ct),
            await AlmoxarifadoAsync(unidade, hoje, agora, ct),
            esteira,
            // gravidade primeiro, espera depois: o urgente que acabou de entrar sobe ao topo
            // sem ninguém reordenar nada — é o critério de aceite 3
            [.. radar.OrderBy(x => x.Ordem).ThenByDescending(x => x.TempoRestanteOuAtraso.Length).Take(TetoDoRadar)],
            [.. burndown.Where(b => b.Value.Total > 0)
                .OrderByDescending(b => b.Value.Criticas).ThenByDescending(b => b.Value.Total)
                .Select(b => new BurndownDoComprador(b.Key, b.Value.Atendidos, b.Value.Total, b.Value.Criticas))],
            await AgendaDaDocaAsync(hoje, ct));
    }

    /// <summary>
    /// A compra do mês. Nenhuma régua nasce aqui: o valor é o <c>poTotalValue</c> do painel,
    /// o emergencial é o <b>CP-02</b> do Compliance e o teto é a meta do catálogo, comparada
    /// pela mesma função que o painel e a diretoria perguntam. O que este método faz é juntá-las
    /// no recorte da parede — o mês corrente e a unidade da vez.
    ///
    /// <para>
    /// As SCs de origem do pedido vêm de <see cref="Quotation.SourcePrIds"/>, a mesma propriedade
    /// que o Compliance usa para achar as SCs de um processo, mais a SC do pedido lançado direto.
    /// É por elas que o pedido responde pela unidade — a regra já registrada de que "o pedido
    /// responde por todas as SCs de onde veio" — e é nelas que a prioridade <c>URGENT</c> mora.
    /// </para>
    /// </summary>
    private async Task<CompraDoMes> CompraDoMesAsync(
        string? unidade, DateTimeOffset agora, CancellationToken ct)
    {
        var primeiroDoMes = new DateOnly(agora.Year, agora.Month, 1);
        var inicioDoMes = new DateTimeOffset(primeiroDoMes.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // o pedido nasce na aprovação do Nível 2, então `CreatedAt` é a data da aprovação —
        // é essa a âncora que a definição do indicador declara. Cancelado não é compra.
        var pedidos = await db.PurchaseOrders
            .Where(o => o.CreatedAt >= inicioDoMes && o.Status != PurchaseOrderStatus.Cancelled)
            .ToListAsync(ct);

        var cotacaoIds = pedidos.Where(o => o.QuotationId != null)
            .Select(o => o.QuotationId!.Value).Distinct().ToArray();
        // `SourcePrIds` exige os itens carregados: é deles que saem as SCs agrupadas
        var scsDaCotacao = (await db.Quotations.Include(q => q.Items)
                .Where(q => cotacaoIds.Contains(q.Id)).ToListAsync(ct))
            .ToDictionary(q => q.Id, q => q.SourcePrIds);

        List<Guid> ScsDoPedido(PurchaseOrder o)
        {
            var ids = new List<Guid>();
            if (o.SourcePrId is { } direta) ids.Add(direta);
            if (o.QuotationId is { } qid && scsDaCotacao.TryGetValue(qid, out var doProcesso)) ids.AddRange(doProcesso);
            return [.. ids.Distinct()];
        }

        var scIds = pedidos.SelectMany(ScsDoPedido).Distinct().ToArray();
        var scs = (await db.Requisitions.Where(r => scIds.Contains(r.Id))
                .Select(r => new { r.Id, r.Company, r.Priority }).ToListAsync(ct))
            .ToDictionary(x => x.Id);

        if (unidade is not null)
            pedidos = [.. pedidos.Where(o => ScsDoPedido(o)
                .Any(id => scs.TryGetValue(id, out var sc) && sc.Company == unidade))];

        var valor = pedidos.Sum(o => o.TotalValue);
        // "URGENT" é exatamente o que o CP-02 do Compliance lê para dizer "compra emergencial";
        // escrever outro critério aqui daria duas definições do mesmo fato
        var emergenciais = pedidos.Count(o => ScsDoPedido(o)
            .Any(id => scs.TryGetValue(id, out var sc) && sc.Priority == "URGENT"));

        // o teto é o do período decorrido, pela régua do catálogo: a meta que acumula se
        // reparte pelos dias, e cobrar o mês inteiro no dia 3 diria que toda semana está folgada
        var indicador = MetasDosIndicadores.Do(TetoDeCompra);
        var metaMensal = await db.IndicatorGoals.Where(g => g.Indicator == TetoDeCompra)
            .Select(g => (decimal?)g.MonthlyValue).FirstOrDefaultAsync(ct);
        var teto = indicador is null || metaMensal is null ? null
            : MetasDosIndicadores.Comparar(indicador, metaMensal.Value, valor,
                MetasDosIndicadores.MesesDoPeriodo(primeiroDoMes, DateOnly.FromDateTime(agora.UtcDateTime)));

        return new CompraDoMes(
            decimal.Round(valor, 2), pedidos.Count, emergenciais,
            teto is null ? null : decimal.Round(teto.MetaDoPeriodo, 2),
            teto is null ? null : decimal.Round((decimal)teto.Atingimento, 1),
            teto?.Faixa);
    }

    /// <summary>O indicador do catálogo que dá o teto da compra — o mesmo que o painel compara.</summary>
    private const string TetoDeCompra = "poTotalValue";

    /// <summary>
    /// O bloco do almoxarifado. As duas filas do material, o que saiu hoje, quanto o estoque
    /// deu conta e quanto tempo levou — do que a própria solicitação de material grava.
    ///
    /// <para>
    /// O recorte por unidade <b>não sai da solicitação de material</b>: ela não tem empresa. Sai
    /// do centro de custo — <c>CostCenter.CompanyId</c> aponta o CNPJ, e o nome oficial dele é o
    /// mesmo texto que a SC grava em <c>Company</c>, que é o que a TV gira. Centro sem empresa
    /// cadastrada conta na visão geral e fica fora dos recortes: pôr a solicitação numa unidade
    /// escolhida ao acaso seria inventar o dado que falta.
    /// </para>
    /// </summary>
    internal async Task<AlmoxarifadoDoCockpit> AlmoxarifadoAsync(
        string? unidade, DateOnly hoje, DateTimeOffset agora, CancellationToken ct)
    {
        var inicioDoMes = new DateTimeOffset(new DateTime(agora.Year, agora.Month, 1), TimeSpan.Zero);

        // filtro na entidade e projeção por último, como manda a régua do Npgsql: as duas filas
        // abertas e o que foi atendido no mês, num recorte só
        var solicitacoes = await db.MaterialRequisitions.Include(r => r.Items)
            .Where(r => r.Status == MaterialRequisitionStatus.Submitted
                        || r.Status == MaterialRequisitionStatus.Approved
                        || (r.FulfilledAt != null && r.FulfilledAt >= inicioDoMes))
            .ToListAsync(ct);

        if (unidade is not null)
        {
            var empresaPorCentro = await EmpresaPorCentroAsync(ct);
            solicitacoes = [.. solicitacoes.Where(r =>
                empresaPorCentro.GetValueOrDefault(r.CostCenter.Trim().ToUpperInvariant()) == unidade)];
        }

        var fila = solicitacoes.Where(r => r.Status == MaterialRequisitionStatus.Approved).ToList();
        // a espera conta da liberação do Nível 1, que é quando a solicitação entrou nesta fila.
        // Sem essa marca ela fica sem data, em vez de contar como espera do almoxarifado o tempo
        // em que a solicitação ainda estava com o gestor do centro
        var liberadas = fila.Where(r => r.ApprovedAt is not null).OrderBy(r => r.ApprovedAt).ToList();
        var horasDoMaisAntigo = liberadas.Count == 0 ? 0
            : (int)Math.Max(0, (agora - liberadas[0].ApprovedAt!.Value).TotalHours);

        var atendidas = solicitacoes.Where(r => r.FulfilledAt is { } f && f >= inicioDoMes).ToList();
        var aprovado = atendidas.Sum(r => r.Items.Sum(i => i.EffectiveQuantity));
        var entregue = atendidas.Sum(r => r.Items.Sum(i => i.FulfilledQuantity));
        var ciclos = atendidas.Where(r => r.ApprovedAt is not null)
            .Select(r => (decimal)(r.FulfilledAt!.Value - r.ApprovedAt!.Value).TotalHours).ToList();

        return new AlmoxarifadoDoCockpit(
            FilaSolicitacoes: fila.Count,
            FilaItens: fila.Sum(r => r.Items.Count),
            HorasDoMaisAntigo: horasDoMaisAntigo,
            // a mesma régua de cor da esteira: uma parede com dois critérios de "está travado"
            // obrigaria quem passa a lembrar qual vale para qual bloco
            Gargalo: GargaloDe(fila.Count, horasDoMaisAntigo),
            MaisAntigaNumero: liberadas.FirstOrDefault()?.Number,
            AguardandoAprovacao: solicitacoes.Count(r => r.Status == MaterialRequisitionStatus.Submitted),
            AtendidasHoje: atendidas.Count(r => DateOnly.FromDateTime(r.FulfilledAt!.Value.UtcDateTime) == hoje),
            AtendidoPeloEstoquePct: aprovado <= 0 ? null : decimal.Round(entregue * 100m / aprovado, 1),
            ViraramCompraNoMes: atendidas.Count(r => r.PurchaseRequisitionId is not null),
            HorasMediaAtendimento: ciclos.Count == 0 ? null : decimal.Round(ciclos.Average(), 1));
    }

    /// <summary>
    /// Centro de custo (em caixa alta) → nome oficial da empresa. É o único caminho da
    /// solicitação de material até a unidade da parede, e as duas tabelas são de cadastro:
    /// juntar em memória custa menos que um <c>Join</c> sobre chave anulável.
    /// </summary>
    internal async Task<Dictionary<string, string>> EmpresaPorCentroAsync(CancellationToken ct)
    {
        var empresas = await db.Companies.Select(e => new { e.Id, e.LegalName }).ToListAsync(ct);
        var centros = await db.CostCenters.Where(c => c.CompanyId != null)
            .Select(c => new { c.Code, c.CompanyId }).ToListAsync(ct);
        var nomePorId = empresas.ToDictionary(e => e.Id, e => e.LegalName);

        var mapa = new Dictionary<string, string>();
        foreach (var centro in centros)
            if (nomePorId.TryGetValue(centro.CompanyId!.Value, out var legal))
                mapa[centro.Code.Trim().ToUpperInvariant()] = legal;
        return mapa;
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
