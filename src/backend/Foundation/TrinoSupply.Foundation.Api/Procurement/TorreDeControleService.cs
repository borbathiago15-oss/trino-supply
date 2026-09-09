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
    decimal? MinValue = null, decimal? MaxValue = null);

/// <summary>Uma linha da Torre: um item de compra, com o seu próprio andamento.</summary>
public record LinhaDaTorre(
    Guid ItemId, Guid RequisitionId, string PrNumber, int Sequence,
    string? CatalogCode, string Description, decimal Quantity, string UnitOfMeasure,
    string RequesterLabel, string? Company, string CostCenter, string? BuyerLabel,
    string? SupplierName, string Stage, string StageLabel,
    string StatusKey, string StatusLabel, string StatusTone,
    string Priority, DateOnly? NeededBy, DateOnly? PromisedDate, bool Late,
    decimal? Value, Guid? QuotationId, string? QuotationNumber,
    Guid? PurchaseOrderId, string? PurchaseOrderNumber);

/// <summary>
/// Os números do topo. Não há "em faturamento" separado de "aguardando recebimento":
/// a situação `OC_FATURAMENTO` cobre as duas — a O.C. saiu e o material não chegou.
/// Repetir o mesmo número com dois nomes daria a impressão de duas filas.
/// </summary>
public record KpisDaTorre(
    int Total, int Novos, int EmCotacao, int AguardandoAprovacao, int AguardandoOc,
    int AguardandoRecebimento, int Atrasados, int Urgentes, decimal Valor);

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
public class TorreDeControleService(AppDbContext db, TimeProvider clock)
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

    public async Task<PaginaDaTorre> ConsultarAsync(FiltroTorre f, CancellationToken ct = default)
    {
        var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
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
                       || f.MinValue is not null || f.MaxValue is not null;
        var ordenada = consulta.OrderByDescending(x => x.sc.CreatedAt).ThenBy(x => x.item.Sequence);

        var total = derivado ? 0 : await consulta.CountAsync(ct);
        var bruto = derivado
            ? await ordenada.Take(TetoDerivado + 1).ToListAsync(ct)
            : await ordenada.Skip((pagina - 1) * tamanho).Take(tamanho).ToListAsync(ct);
        var estourou = derivado && bruto.Count > TetoDerivado;
        if (estourou) bruto = bruto.Take(TetoDerivado).ToList();

        // ---- o andamento das SCs desta página -------------------------------
        var scIds = bruto.Select(x => x.sc.Id).Distinct().ToList();
        var cotacoes = await db.Quotations.Include(q => q.Items).Include(q => q.Awards)
            .Where(q => scIds.Contains(q.SourcePrId)
                        || q.Items.Any(i => i.SourcePrId != null && scIds.Contains(i.SourcePrId.Value)))
            .OrderByDescending(q => q.CreatedAt).ToListAsync(ct);
        var cotacaoIds = cotacoes.Select(q => q.Id).ToList();
        var pedidos = await db.PurchaseOrders.Include(o => o.Items)
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

            linhas.Add(new LinhaDaTorre(
                x.item.Id, x.sc.Id, x.sc.Number, x.item.Sequence,
                x.item.CatalogCode, x.item.Description, x.item.Quantity, x.item.UnitOfMeasure,
                x.sc.RequesterLabel, x.sc.Company, x.sc.CostCenter,
                x.sc.AssignedToLabel ?? pedido?.IssuedByLabel ?? cotacao?.CreatedByLabel,
                pedido?.SupplierName, etapa, RotuloDaEtapa(etapa),
                situacao.Key, situacao.Label, situacao.Tone,
                x.sc.Priority, x.sc.NeededBy, pedido?.PromisedDate, atrasado,
                valor, cotacao?.Id, cotacao?.Number, pedido?.Id, pedido?.Number));
        }

        if (derivado)
        {
            // agora o total é o que sobrou do filtro, e a página sai dele
            total = linhas.Count;
            linhas = linhas.Skip((pagina - 1) * tamanho).Take(tamanho).ToList();
        }

        return new PaginaDaTorre(linhas, await KpisAsync(hoje, ct), await OpcoesAsync(ct),
            pagina, tamanho, total, (int)Math.Ceiling(total / (double)tamanho), estourou, TetoDerivado);
    }

    /// <summary>
    /// Os números do topo, sobre **tudo** o que está aberto — e não sobre a página.
    /// KPI que muda ao virar a página não é indicador, é contagem de tela.
    /// </summary>
    private async Task<KpisDaTorre> KpisAsync(DateOnly hoje, CancellationToken ct)
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
        var pedidos = await db.PurchaseOrders
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
            recebendo = 0, atrasados = 0, urgentes = 0;
        decimal valor = 0;
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

            foreach (var _ in sc.Items)
            {
                total++;
                switch (etapa)
                {
                    case "SOLICITACAO": novos++; break;
                    case "COTACAO": cotando++; break;
                    case "APROVACAO": aprovando++; break;
                    case "ORDEM_DE_COMPRA": aguardandoOc++; break;
                    case "RECEBIMENTO": recebendo++; break;
                }
                if (atrasada) atrasados++;
                if (sc.Priority == "URGENT" && etapa != "ENCERRADO") urgentes++;
            }
            if (etapa != "ENCERRADO") valor += sc.TotalEstimatedValue;
        }

        return new KpisDaTorre(total, novos, cotando, aprovando, aguardandoOc,
            recebendo, atrasados, urgentes, valor);
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
