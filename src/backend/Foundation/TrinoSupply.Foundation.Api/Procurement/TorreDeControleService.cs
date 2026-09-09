using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>Recorte da Torre. Vazio em tudo = tudo o que está aberto.</summary>
public record FiltroTorre(
    string? Search = null, string? Stage = null, string? Status = null,
    string? Company = null, string? CostCenter = null, string? Family = null,
    Guid? RequesterId = null, Guid? BuyerId = null, string? Priority = null,
    DateOnly? From = null, DateOnly? To = null, bool? Late = null,
    int Page = 1, int PageSize = 50,
    // §5.1 — os filtros obrigatórios que faltavam. Os quatro abaixo são derivados
    // (dependem do pedido), então entram pelo caminho com teto, junto de etapa e
    // situação; `From`/`To` continuam em SQL, sobre a data de criação da SC.
    string? Supplier = null, string? OrderNumber = null,
    DateOnly? DueFrom = null, DateOnly? DueTo = null,
    decimal? MinValue = null, decimal? MaxValue = null,
    /// <summary>
    /// Faixa de tempo na fila (0 = 0–2 dias, 1 = 3–5, 2 = 6–10, 3 = mais de 10). Veio da
    /// tela de triagem, que era onde o comprador via há quanto tempo a demanda espera —
    /// e que era a única razão de aquela tela existir em paralelo a esta.
    /// </summary>
    int? AgingBand = null,
    /// <summary>Só o que virou exceção — ver <see cref="ExcecaoDe"/>.</summary>
    bool? Exception = null,
    /// <summary>A fila prioritária do §5: só o que espera ação do comprador.</summary>
    bool? NeedsBuyer = null,
    /// <summary>
    /// Separa as duas filas do recebimento, que é o que a nota fiscal faz: <c>true</c> é
    /// O.C. emitida sem NF (a bola está com o fornecedor), <c>false</c> é NF lançada e
    /// material não recebido (a bola está com o almoxarifado). Nulo traz as duas.
    ///
    /// <para>
    /// Existe porque os dois números do topo eram contados em separado e caíam no mesmo
    /// filtro: clicar em "Em faturamento: 3" mostrava as dez linhas das duas filas, e o
    /// card passava a mentir sobre a própria lista.
    /// </para>
    /// </summary>
    bool? Invoicing = null);

/// <summary>Uma linha da Torre: um item de compra, com o seu próprio andamento.</summary>
public record LinhaDaTorre(
    Guid ItemId, Guid RequisitionId, string PrNumber, int Sequence,
    string? CatalogCode, string Description, decimal Quantity, string UnitOfMeasure,
    string RequesterLabel, string? Company, string CostCenter, string? BuyerLabel,
    string? SupplierName, string Stage, string StageLabel,
    string StatusKey, string StatusLabel, string StatusTone,
    string Priority, DateOnly? NeededBy, DateOnly? PromisedDate, bool Late,
    decimal? Value, Guid? QuotationId, string? QuotationNumber,
    Guid? PurchaseOrderId, string? PurchaseOrderNumber,
    /// <summary>
    /// Quando o item entrou na fila do comprador: a decisão da SC, ou o envio enquanto
    /// ela não foi decidida. É a mesma data que a fila de cotação já usa para ordenar —
    /// duas contas diferentes para "há quanto tempo espera" dariam dois números.
    /// </summary>
    DateTimeOffset? OpenedAt = null,
    /// <summary>Por que este item é exceção, ou nulo quando segue o caminho normal.</summary>
    string? ExceptionReason = null,
    /// <summary>A próxima ação esperada — o que a linha pede que se faça agora.</summary>
    string? ActionLabel = null,
    /// <summary>Se essa ação é do comprador (é o que define a fila prioritária).</summary>
    bool NeedsBuyer = false,
    /// <summary>De quem a linha está esperando, e há quanto tempo. Nulo quando não se espera nada.</summary>
    EsperaDaLinha? WaitingOn = null);

/// <summary>
/// Os números do topo (§5).
///
/// <para>
/// <b>"Em faturamento" e "aguardando recebimento" são filas diferentes</b>, e o que as
/// separa é a nota fiscal: com a O.C. emitida e nenhuma NF lançada, quem deve agir é o
/// fornecedor (falta faturar); com a NF lançada e o material não recebido, quem age é o
/// almoxarifado. Antes as duas viviam no mesmo número, e o comprador não sabia para
/// quem cobrar.
/// </para>
/// </summary>
public record KpisDaTorre(
    int Total, int Novos, int EmCotacao, int AguardandoAprovacao, int AguardandoOc,
    int AguardandoRecebimento, int Atrasados, int Urgentes, decimal Valor,
    int EmFaturamento = 0, int Excecoes = 0,
    /// <summary>
    /// Quantos itens esperam ação do comprador — pela <b>mesma</b> regra do filtro
    /// (<see cref="AcaoDe"/>), e não por uma soma de etapas montada à parte.
    ///
    /// <para>
    /// Somar "novos + em cotação + aguardando O.C." dava um número parecido e errado: deixava
    /// de fora a exceção em etapa de aprovação ou recebimento, que volta ao comprador. O card
    /// dizia 12 e a lista mostrava 14.
    /// </para>
    /// </summary>
    int PrecisaDeVoce = 0,
    /// <summary>Quantos itens em cada faixa de fila, na ordem de <see cref="FaixasDeAging"/>.</summary>
    IReadOnlyList<int>? PorFaixaDeAging = null);

public record OpcoesDaTorre(
    IReadOnlyList<string> Companies, IReadOnlyList<OpcaoCodigo> CostCenters,
    IReadOnlyList<string> Families, IReadOnlyList<OpcaoPessoa> Requesters,
    IReadOnlyList<OpcaoPessoa> Buyers);
public record OpcaoCodigo(string Code, string Name);
public record OpcaoPessoa(Guid Id, string Label);

public record PaginaDaTorre(
    IReadOnlyList<LinhaDaTorre> Items, KpisDaTorre Kpis, OpcoesDaTorre FilterOptions,
    int Page, int PageSize, int Total, int Pages, bool Capped, int Cap);

/// <summary>
/// Torre de Controle do comprador: **uma linha por item**, nunca uma por solicitação.
///
/// Uma SC de cinco itens pode ter um item já entregue, dois em cotação e dois
/// parados esperando o comprador. Olhar a SC inteira esconde exatamente o que
/// precisa de ação, e é por isso que a unidade aqui é o item.
///
/// ## Etapa e Situação são derivadas, não gravadas
///
/// A etapa (onde o item está) e a situação (como está) saem do estado que já
/// existe — solicitação, cotação e pedido. Gravá-las por item criaria uma
/// terceira fonte de verdade que pode discordar das outras duas, e quando
/// discordasse ninguém saberia qual está certa. Derivar custa CPU na leitura;
/// gravar custa confiança.
///
/// ## A paginação é no banco, e não é enfeite
///
/// A fila de cotação carrega as 200 mais antigas e corta o resto — com a base
/// crescendo, a demanda **recente** é a que some. Esta tela nasce diferente: o
/// filtro e o corte da página acontecem em SQL, sobre o item, e o total vem por
/// `COUNT`. É a tela que o comprador deixa aberta o dia inteiro; não pode ser a
/// que traz tudo para descartar quase tudo.
/// </summary>
public partial class TorreDeControleService(AppDbContext db, TimeProvider clock)
{
    public const int PaginaMaxima = 200;
    /// <summary>Teto do caminho com filtro derivado — ver `ConsultarAsync`.</summary>
    public const int TetoDerivado = 3000;

    /// <summary>Quem trabalha a fila de compras — e quem audita o que ela mostra.</summary>
    public static bool CanView(string role) =>
        role is Roles.PurchasingOfficer or Roles.SupplyManager or Roles.SystemAdministrator
             or Roles.Director or Roles.Auditor;

    /// <summary>As etapas do fluxo, na ordem em que o item as percorre.</summary>
    public static readonly (string Key, string Label)[] Etapas =
    [
        ("SOLICITACAO", "Solicitação"),
        ("COTACAO", "Cotação"),
        ("APROVACAO", "Aprovação"),
        ("ORDEM_DE_COMPRA", "Ordem de Compra"),
        ("RECEBIMENTO", "Recebimento"),
        ("ENCERRADO", "Encerrado"),
    ];

    /// <summary>
    /// Da situação para a etapa. As duas respondem perguntas diferentes — "como
    /// está" e "onde está" —, mas a segunda é função da primeira: não há item
    /// "em cotação" cuja situação diga outra coisa.
    /// </summary>
    public static string EtapaDe(string statusKey) => statusKey switch
    {
        "RASCUNHO" or "PENDENTE" or "DEVOLVIDO" => "SOLICITACAO",
        "EM_COTACAO" => "COTACAO",
        "AGUARDANDO_APROVACAO" => "APROVACAO",
        "PEDIDO_APROVADO" => "ORDEM_DE_COMPRA",
        "OC_FATURAMENTO" => "RECEBIMENTO",
        _ => "ENCERRADO",   // entregue, rejeitado, cancelado/parcial
    };

    public static string RotuloDaEtapa(string chave) =>
        Etapas.FirstOrDefault(e => e.Key == chave).Label ?? chave;

    /// <summary>
    /// As faixas de tempo na fila, em dias corridos. São as mesmas quatro que a triagem
    /// usava — mantê-las idênticas é o que permite as duas telas virarem uma sem que
    /// nenhum número mude de significado no caminho.
    /// </summary>
    public static readonly int[] FaixasDeAging = [2, 5, 10, int.MaxValue];

    /// <summary>Índice da faixa: 0 é a mais nova, 3 a que espera há mais de dez dias.</summary>
    public static int FaixaDeAging(DateTimeOffset? desde, DateTimeOffset agora)
    {
        if (desde is null) return 0;
        var dias = Math.Max(0, (int)(agora - desde.Value).TotalDays);
        for (var i = 0; i < FaixasDeAging.Length; i++)
            if (dias <= FaixasDeAging[i]) return i;
        return FaixasDeAging.Length - 1;
    }

    /// <summary>
    /// Por que este item é exceção — ou nulo quando ele segue o caminho normal.
    ///
    /// <para>
    /// O documento pede um KPI de "exceções" e prevê um fluxo próprio para registrá-las,
    /// que ainda não existe. Em vez de inventar um registro vazio, o número sai do que o
    /// sistema <b>já grava</b> como fora do padrão — e cada um destes tem regra e trilha
    /// de auditoria atrás:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item>compra fechada sem O.C. do ERP, com a justificativa do PO-BR-011;</item>
    /// <item>pedido cancelado ou com saldo encerrado sem entrega completa;</item>
    /// <item>material recebido e devolvido ao fornecedor.</item>
    /// </list>
    ///
    /// A ordem importa: um pedido pode ser mais de uma coisa ao mesmo tempo, e o
    /// comprador precisa ver primeiro o que o obriga a agir.
    /// </summary>
    public static string? ExcecaoDe(PurchaseOrder? pedido)
    {
        if (pedido is null) return null;
        if (pedido.Status == PurchaseOrderStatus.Cancelled) return "Pedido cancelado";
        if (pedido.Status == PurchaseOrderStatus.PartiallyReceived) return "Saldo encerrado sem entrega completa";
        if (pedido.Items.Any(i => i.RejectedQuantity > 0)) return "Material devolvido ao fornecedor";
        if (!string.IsNullOrWhiteSpace(pedido.NoErpReason)) return "Fechado sem O.C. do ERP";
        return null;
    }

    /// <summary>
    /// Qual é a próxima ação esperada do item, e de quem ela é.
    ///
    /// <para>
    /// O documento pede "ações rápidas" na linha e uma "fila prioritária" com os itens
    /// que exigem ação do comprador — as duas coisas são a mesma pergunta, respondida
    /// aqui uma vez só. Fossem duas regras, a fila e o botão discordariam no primeiro
    /// caso de canto, e o comprador não confiaria em nenhum dos dois.
    /// </para>
    ///
    /// <para>
    /// <b>Exceção sempre volta para o comprador</b>, em qualquer etapa: pedido cancelado,
    /// saldo encerrado, devolução e fechamento sem O.C. do ERP são justamente os casos
    /// que ninguém mais resolve sozinho.
    /// </para>
    /// </summary>
    public static (string Label, bool DoComprador) AcaoDe(
        string etapa, string? excecao, bool temComprador)
    {
        if (excecao is not null) return ("Tratar exceção", true);
        return etapa switch
        {
            "SOLICITACAO" when !temComprador => ("Atribuir comprador", true),
            "SOLICITACAO" => ("Abrir cotação", true),
            "COTACAO" => ("Conduzir cotação", true),
            // a bola está com os aprovadores: aparece na linha, mas não na fila do comprador
            "APROVACAO" => ("Acompanhar aprovação", false),
            "ORDEM_DE_COMPRA" => ("Registrar O.C. do ERP", true),
            "RECEBIMENTO" => ("Faturamento e entrega", false),
            _ => ("", false),
        };
    }

    /// <summary>
    /// A O.C. saiu e ainda não há nota: quem deve agir é o fornecedor. Com NF lançada e
    /// material não recebido, a bola passa ao almoxarifado — são filas diferentes.
    /// </summary>
    public static bool EmFaturamento(PurchaseOrder? pedido) =>
        pedido is not null
        && pedido.Status is not (PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled)
        && pedido.Invoices.Count == 0;

    public async Task<PaginaDaTorre> ConsultarAsync(FiltroTorre f, CancellationToken ct = default)
    {
        var agora = clock.GetUtcNow();
        var hoje = DateOnly.FromDateTime(agora.UtcDateTime);
        var tamanho = Math.Clamp(f.PageSize, 1, PaginaMaxima);
        var pagina = Math.Max(1, f.Page);

        // ---- o recorte, em SQL ---------------------------------------------
        // O que dá para filtrar no banco fica no banco. Etapa, situação e atraso
        // dependem de cotação e pedido, então são aplicados depois — mas sobre a
        // página, não sobre a base inteira.
        var consulta = from item in db.RequisitionItems
                       join sc in db.Requisitions on item.RequisitionId equals sc.Id
                       where sc.DeletedAt == null && sc.Status != RequisitionStatus.Draft
                       select new { item, sc };

        if (f.RequesterId is { } sol) consulta = consulta.Where(x => x.sc.RequesterId == sol);
        if (f.BuyerId is { } comp) consulta = consulta.Where(x => x.sc.AssignedToId == comp);
        if (f.Company is { Length: > 0 } emp) consulta = consulta.Where(x => x.sc.Company == emp);
        if (f.CostCenter is { Length: > 0 } cc) consulta = consulta.Where(x => x.sc.CostCenter == cc);
        if (f.Priority is { Length: > 0 } pri) consulta = consulta.Where(x => x.sc.Priority == pri);
        if (f.From is { } de)
            consulta = consulta.Where(x => x.sc.CreatedAt >= new DateTimeOffset(de.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        if (f.To is { } ate)
            consulta = consulta.Where(x => x.sc.CreatedAt < new DateTimeOffset(ate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        if (f.Search is { Length: > 0 } busca)
        {
            // `ToLower().Contains` e não `ILike`: o ILike é do Npgsql e some no provedor
            // de teste, e busca que só existe em produção é busca que ninguém testou
            var termo = busca.Trim().ToLowerInvariant();
            consulta = consulta.Where(x =>
                x.sc.Number.ToLower().Contains(termo) ||
                x.item.Description.ToLower().Contains(termo) ||
                (x.item.CatalogCode != null && x.item.CatalogCode.ToLower().Contains(termo)));
        }

        // Etapa, situação, família e atraso não existem em coluna: dependem da cotação
        // e do pedido. Filtrar por eles **depois** do corte da página devolveria três
        // linhas numa página de cinquenta, com o total dizendo quatrocentas — a tela
        // mentiria sobre o próprio filtro. Então há dois caminhos:
        //
        // - sem filtro derivado (o caso comum, a tela recém-aberta): corte e contagem
        //   em SQL, que é o que mantém a Torre barata;
        // - com filtro derivado: carrega o recorte já filtrado no banco até o teto,
        //   deriva, filtra e pagina em memória — e diz quando bateu no teto, em vez
        //   de calar como faz a fila de cotação.
        var derivado = f.Stage is { Length: > 0 } || f.Status is { Length: > 0 }
                       || f.Family is { Length: > 0 } || f.Late == true
                       || f.Supplier is { Length: > 0 } || f.OrderNumber is { Length: > 0 }
                       || f.DueFrom is not null || f.DueTo is not null
                       || f.MinValue is not null || f.MaxValue is not null
                       || f.Exception == true || f.NeedsBuyer == true || f.Invoicing is not null;
        var ordenada = consulta.OrderByDescending(x => x.sc.CreatedAt).ThenBy(x => x.item.Sequence);

        var total = derivado ? 0 : await consulta.CountAsync(ct);
        var bruto = derivado
            ? await ordenada.Take(TetoDerivado + 1).ToListAsync(ct)
            : await ordenada.Skip((pagina - 1) * tamanho).Take(tamanho).ToListAsync(ct);
        var estourou = derivado && bruto.Count > TetoDerivado;
        if (estourou) bruto = bruto.Take(TetoDerivado).ToList();

        // ---- o andamento das SCs desta página -------------------------------
        var scIds = bruto.Select(x => x.sc.Id).Distinct().ToList();
        // convites e propostas entram porque a espera da linha é derivada deles: sem o
        // Include, `SituacaoDosConvites` veria coleção vazia e diria que não falta ninguém
        var cotacoes = await db.Quotations.Include(q => q.Items).Include(q => q.Awards)
            .Include(q => q.Suppliers).Include(q => q.Proposals)
            .Where(q => scIds.Contains(q.SourcePrId)
                        || q.Items.Any(i => i.SourcePrId != null && scIds.Contains(i.SourcePrId.Value)))
            .OrderByDescending(q => q.CreatedAt).ToListAsync(ct);
        var cotacaoIds = cotacoes.Select(q => q.Id).ToList();
        var pedidos = await db.PurchaseOrders.Include(o => o.Items).Include(o => o.Invoices)
            .Where(o => (o.SourcePrId != null && scIds.Contains(o.SourcePrId.Value))
                        || (o.QuotationId != null && cotacaoIds.Contains(o.QuotationId.Value)))
            .OrderByDescending(o => o.CreatedAt).ToListAsync(ct);

        // dicionários antes do laço: `FirstOrDefault` por linha faria a busca varrer a
        // lista inteira a cada item — o custo cresceria com o quadrado do movimento
        var cotacaoPorSc = new Dictionary<Guid, Quotation>();
        foreach (var q in cotacoes)
            foreach (var id in scIds.Where(q.CoversPr))
                cotacaoPorSc.TryAdd(id, q);
        var pedidoPorSc = new Dictionary<Guid, PurchaseOrder>();
        var pedidoPorCotacao = new Dictionary<Guid, PurchaseOrder>();
        foreach (var o in pedidos)
        {
            if (o.SourcePrId is { } pid) pedidoPorSc.TryAdd(pid, o);
            if (o.QuotationId is { } qid) pedidoPorCotacao.TryAdd(qid, o);
        }

        var familiaPorProduto = await db.CatalogItems
            .Select(i => new { i.Id, i.Family }).ToDictionaryAsync(x => x.Id, x => x.Family, ct);

        // os aprovadores dos centros desta página, de uma vez: perguntar por linha faria
        // uma consulta por item, e a Torre é justamente a tela com muitas linhas
        var centros = bruto.Select(x => x.sc.CostCenter.Trim().ToUpperInvariant())
            .Where(c => c.Length > 0).Distinct().ToList();
        var alcadaPorCentro = centros.Count == 0
            ? []
            : await db.CostCenters.Where(c => centros.Contains(c.Code.ToUpper()))
                .Select(c => new
                {
                    c.Code,
                    N1 = c.Approvers.Where(a => a.Level == ApprovalLevels.Level1).Select(a => a.UserName).ToList(),
                    N2 = c.Approvers.Where(a => a.Level == ApprovalLevels.Level2).Select(a => a.UserName).ToList(),
                })
                .ToDictionaryAsync(x => x.Code.ToUpperInvariant(),
                    x => new AlcadasDoCentro(x.N1, x.N2), ct);

        var linhas = new List<LinhaDaTorre>(bruto.Count);
        foreach (var x in bruto)
        {
            cotacaoPorSc.TryGetValue(x.sc.Id, out var cotacao);
            PurchaseOrder? pedido = null;
            if (!pedidoPorSc.TryGetValue(x.sc.Id, out pedido) && cotacao is not null)
                pedidoPorCotacao.TryGetValue(cotacao.Id, out pedido);

            var situacao = ProcessStatus.Of(x.sc, cotacao, pedido);
            var etapa = EtapaDe(situacao.Key);
            var encerrado = etapa == "ENCERRADO";

            // atraso é sobre o que ainda não chegou: item entregue não fica atrasado
            var previsao = pedido?.PromisedDate ?? x.sc.NeededBy;
            var atrasado = !encerrado && previsao is not null && previsao < hoje;

            // o valor do item vem do pedido quando ele existe (é o que foi pago);
            // até lá, o estimado da solicitação é o melhor que se tem
            var itemDoPedido = pedido?.Items.FirstOrDefault(i =>
                (x.item.CatalogItemId is not null && i.CatalogItemId == x.item.CatalogItemId)
                || i.Description == x.item.Description);
            var valor = itemDoPedido is not null
                ? (itemDoPedido.UnitPrice ?? 0) * itemDoPedido.Quantity
                : x.item.EstimatedUnitPrice is { } preco ? preco * x.item.Quantity : (decimal?)null;

            var familia = x.item.CatalogItemId is { } cid && familiaPorProduto.TryGetValue(cid, out var fam)
                ? fam : null;
            if (f.Family is { Length: > 0 } filtroFamilia
                && !string.Equals(familia, filtroFamilia, StringComparison.OrdinalIgnoreCase)) continue;
            if (f.Stage is { Length: > 0 } filtroEtapa && etapa != filtroEtapa) continue;
            if (f.Status is { Length: > 0 } filtroStatus && situacao.Key != filtroStatus) continue;
            if (f.Late == true && !atrasado) continue;

            // §5.1: fornecedor, número da O.C., faixa de prazo e faixa de valor.
            // Item sem pedido ainda não tem fornecedor nem O.C. — filtrar por eles é
            // pedir "só o que já foi comprado", então o item sem pedido sai fora.
            if (f.Supplier is { Length: > 0 } filtroForn
                && (pedido?.SupplierName is null
                    || !pedido.SupplierName.Contains(filtroForn, StringComparison.OrdinalIgnoreCase))) continue;
            if (f.OrderNumber is { Length: > 0 } filtroOc)
            {
                // casa com a numeração própria e com a do ERP: quem procura "4521"
                // tem na mão o número que o ERP devolveu, não o nosso
                var bateOc = (pedido?.Number is { } n && n.Contains(filtroOc, StringComparison.OrdinalIgnoreCase))
                             || (pedido?.ErpNumber is { } e && e.Contains(filtroOc, StringComparison.OrdinalIgnoreCase));
                if (!bateOc) continue;
            }
            // a faixa de prazo é sobre a **previsão exibida** na linha (a data prometida
            // pelo fornecedor, ou a de necessidade enquanto ela não existe) — filtrar por
            // outra data mostraria linhas que contradizem a coluna ao lado
            if (f.DueFrom is { } prazoDe && (previsao is null || previsao < prazoDe)) continue;
            if (f.DueTo is { } prazoAte && (previsao is null || previsao > prazoAte)) continue;
            if (f.MinValue is { } minimo && (valor is null || valor < minimo)) continue;
            if (f.MaxValue is { } maximo && (valor is null || valor > maximo)) continue;

            var excecao = ExcecaoDe(pedido);
            if (f.Exception == true && excecao is null) continue;

            var (acao, doComprador) = AcaoDe(etapa, excecao, x.sc.AssignedToId is not null);
            if (f.NeedsBuyer == true && !doComprador) continue;
            if (f.Invoicing is { } semNota && EmFaturamento(pedido) != semNota) continue;

            var naFilaDesde = x.sc.DecidedAt ?? x.sc.SubmittedAt;
            if (f.AgingBand is { } faixa && FaixaDeAging(naFilaDesde, agora) != faixa) continue;

            var espera = EsperaDe(x.sc, cotacao, pedido,
                alcadaPorCentro.GetValueOrDefault(x.sc.CostCenter.Trim().ToUpperInvariant(),
                    AlcadasDoCentro.Nenhuma), hoje, agora);

            linhas.Add(new LinhaDaTorre(
                x.item.Id, x.sc.Id, x.sc.Number, x.item.Sequence,
                x.item.CatalogCode, x.item.Description, x.item.Quantity, x.item.UnitOfMeasure,
                x.sc.RequesterLabel, x.sc.Company, x.sc.CostCenter,
                x.sc.AssignedToLabel ?? pedido?.IssuedByLabel ?? cotacao?.CreatedByLabel,
                pedido?.SupplierName, etapa, RotuloDaEtapa(etapa),
                situacao.Key, situacao.Label, situacao.Tone,
                x.sc.Priority, x.sc.NeededBy, pedido?.PromisedDate, atrasado,
                valor, cotacao?.Id, cotacao?.Number, pedido?.Id, pedido?.Number,
                naFilaDesde, excecao, acao.Length == 0 ? null : acao, doComprador, espera));
        }

        if (derivado)
        {
            // a fila prioritária tem ordem própria (§5): atrasado primeiro, depois o
            // urgente, e dentro disso o prazo mais apertado. Ordenar pela criação, como
            // no resto da Torre, deixaria o item que vence amanhã atrás do que chegou
            // hoje — que é o oposto de uma fila de prioridade.
            if (f.NeedsBuyer == true)
                linhas = [.. linhas
                    .OrderByDescending(l => l.Late)
                    .ThenByDescending(l => l.Priority == "URGENT")
                    .ThenBy(l => l.PromisedDate ?? l.NeededBy ?? DateOnly.MaxValue)];

            // agora o total é o que sobrou do filtro, e a página sai dele
            total = linhas.Count;
            linhas = linhas.Skip((pagina - 1) * tamanho).Take(tamanho).ToList();
        }

        return new PaginaDaTorre(linhas, await KpisAsync(hoje, agora, ct), await OpcoesAsync(ct),
            pagina, tamanho, total, (int)Math.Ceiling(total / (double)tamanho), estourou, TetoDerivado);
    }

    /// <summary>
    /// Os números do topo, sobre **tudo** o que está aberto — e não sobre a página.
    /// KPI que muda ao virar a página não é indicador, é contagem de tela.
    /// </summary>
    private async Task<KpisDaTorre> KpisAsync(DateOnly hoje, DateTimeOffset agora, CancellationToken ct)
    {
        var abertas = await db.Requisitions.Include(r => r.Items)
            .Where(r => r.DeletedAt == null && r.Status != RequisitionStatus.Draft)
            .OrderByDescending(r => r.CreatedAt).Take(2000).ToListAsync(ct);
        var ids = abertas.Select(r => r.Id).ToList();
        var cotacoes = await db.Quotations.Include(q => q.Items)
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

        int total = 0, novos = 0, cotando = 0, aprovando = 0, aguardandoOc = 0,
            recebendo = 0, atrasados = 0, urgentes = 0, faturando = 0, excecoes = 0,
            precisaDeVoce = 0;
        decimal valor = 0;
        var porFaixa = new int[FaixasDeAging.Length];
        foreach (var sc in abertas)
        {
            cotacaoPorSc.TryGetValue(sc.Id, out var cotacao);
            PurchaseOrder? pedido = null;
            if (!pedidoPorSc.TryGetValue(sc.Id, out pedido) && cotacao is not null)
                pedidoPorCotacao.TryGetValue(cotacao.Id, out pedido);
            var situacao = ProcessStatus.Of(sc, cotacao, pedido);
            var etapa = EtapaDe(situacao.Key);
            var previsao = pedido?.PromisedDate ?? sc.NeededBy;
            var atrasada = etapa != "ENCERRADO" && previsao is not null && previsao < hoje;
            var aguardandoNf = EmFaturamento(pedido);
            var motivoDaExcecao = ExcecaoDe(pedido);
            var excecao = motivoDaExcecao is not null;
            // o KPI usa a mesma regra do filtro: o número do card e o tamanho da lista
            // que ele abre precisam sair da mesma pergunta
            var doComprador = AcaoDe(etapa, motivoDaExcecao, sc.AssignedToId is not null).DoComprador;

            foreach (var _ in sc.Items)
            {
                total++;
                switch (etapa)
                {
                    case "SOLICITACAO": novos++; break;
                    case "COTACAO": cotando++; break;
                    case "APROVACAO": aprovando++; break;
                    case "ORDEM_DE_COMPRA": aguardandoOc++; break;
                    // a etapa é a mesma; o que separa as duas filas é a nota fiscal
                    case "RECEBIMENTO" when aguardandoNf: faturando++; break;
                    case "RECEBIMENTO": recebendo++; break;
                }
                if (excecao) excecoes++;
                if (doComprador) precisaDeVoce++;
                if (atrasada) atrasados++;
                // o aging conta a espera de quem ainda está na fila: item encerrado
                // já não espera por ninguém e inflaria a faixa mais velha para sempre
                if (etapa != "ENCERRADO")
                    porFaixa[FaixaDeAging(sc.DecidedAt ?? sc.SubmittedAt, agora)]++;
                if (sc.Priority == "URGENT" && etapa != "ENCERRADO") urgentes++;
            }
            if (etapa != "ENCERRADO") valor += sc.TotalEstimatedValue;
        }

        return new KpisDaTorre(total, novos, cotando, aprovando, aguardandoOc,
            recebendo, atrasados, urgentes, valor, faturando, excecoes, precisaDeVoce, porFaixa);
    }

    /// <summary>
    /// As opções dos selects. Projetadas em tipo anônimo, e não direto no `record`:
    /// `Distinct()` sobre um record projetado o provedor em memória aceita e o
    /// Postgres não traduz — a consulta quebrava só em produção, que é o pior lugar
    /// para descobrir. O tipo anônimo traduz nos dois, e o record é montado depois.
    /// </summary>
    private async Task<OpcoesDaTorre> OpcoesAsync(CancellationToken ct) => new(
        await db.Requisitions.Where(r => r.Company != null && r.Company != "")
            .Select(r => r.Company!).Distinct().OrderBy(x => x).Take(200).ToListAsync(ct),
        (await db.CostCenters.Where(c => c.Active).OrderBy(c => c.Code)
                .Select(c => new { c.Code, c.Name }).ToListAsync(ct))
            .Select(c => new OpcaoCodigo(c.Code, c.Name)).ToList(),
        await db.CatalogItems.Select(i => i.Family).Distinct().OrderBy(f => f).Take(200).ToListAsync(ct),
        (await db.Requisitions.Select(r => new { r.RequesterId, r.RequesterLabel })
                .Distinct().OrderBy(x => x.RequesterLabel).Take(200).ToListAsync(ct))
            .Select(x => new OpcaoPessoa(x.RequesterId, x.RequesterLabel)).ToList(),
        (await db.Requisitions.Where(r => r.AssignedToId != null && r.AssignedToLabel != null)
                .Select(r => new { Id = r.AssignedToId!.Value, Label = r.AssignedToLabel! })
                .Distinct().OrderBy(x => x.Label).Take(200).ToListAsync(ct))
            .Select(x => new OpcaoPessoa(x.Id, x.Label)).ToList());
}
