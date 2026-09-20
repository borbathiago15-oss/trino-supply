using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

public record ProposalInput(
    int? DeliveryDays, string? PaymentTerms, decimal? FreightValue, DateOnly? ValidUntil,
    string? Notes, IReadOnlyList<ProposalItemInput> Items,
    decimal? DiscountValue = null, string? Currency = null, int? PaymentDays = null,
    decimal? TaxValue = null, decimal? OtherCosts = null, string? PaymentMethodName = null);
/// <summary>SC já designada a um comprador, mas ainda retida na aprovação.</summary>
public record QueueBlocked(PurchaseRequisition Pr, string Reason);

/// <summary>Item da SC ainda sem processo, com a família que define o lote da compra.</summary>
public record QueueItem(Guid Id, int Sequence, string? CatalogCode, string Description,
    decimal Quantity, string UnitOfMeasure, decimal? EstimatedUnitPrice, string Family);

/// <summary>
/// SC na fila de cotação com os itens que ainda NÃO entraram em nenhum processo. A SC só sai da
/// fila quando o último item for cotado: separar EPI de ferramenta em processos diferentes deixa
/// o que sobrou visível aqui, em vez de sumir junto com o primeiro processo aberto.
/// </summary>
public record QueueEntry(PurchaseRequisition Pr, IReadOnlyList<QueueItem> Pending, bool Partial);

public record ProposalItemInput(Guid QuotationItemId, decimal UnitPrice, decimal? Quantity);

/// <summary>Oferta de um fornecedor para uma família inteira (mapa de adjudicação).</summary>
public record FamilyOffer(Guid SupplierId, string SupplierName, Guid ProposalId, int ProposalVersion,
    decimal ItemsValue, decimal TotalValue, int? DeliveryDays, string? PaymentTerms,
    bool Complete, bool Cheapest,
    string Homologation = SupplierHomologation.Homologado, bool Active = true)
{
    /// <summary>
    /// Se esta oferta pode mesmo levar a família — a mesma régua que <c>AwardAsync</c> aplica
    /// na hora de gravar: cotou a família inteira (RFQ-ERR-024), está ativa no cadastro
    /// (RFQ-ERR-040) e está homologada de fato (SUP-ERR-030). A tela usa isto para só
    /// oferecer quem pode vencer, em vez de deixar o comprador descobrir no erro.
    /// </summary>
    public bool CanWin => Complete && Active && Homologation == SupplierHomologation.Homologado;
}

/// <summary>Lote da compra: a família, o que ela pede e quem cotou.</summary>
public record FamilyLot(string Family, int ItemCount, decimal Quantity, IReadOnlyList<FamilyOffer> Offers);

/// <summary>
/// Escolha do fornecedor de UMA família (lote) da cotação. A mesma compra pode ter várias:
/// cada família com o seu vencedor, a sua justificativa e, na ponta, a sua O.C.
/// </summary>
/// <summary>
/// Uma adjudicação pedida pela tela. <see cref="QuotationItemId"/> nulo adjudica a
/// <b>família inteira</b>; preenchido, adjudica <b>aquele item</b> — é o que permite o
/// papel ir para um fornecedor e a caneta para outro, dentro da mesma família.
/// </summary>
/// <param name="Quantity">
/// Quanto do item vai com este fornecedor. Nulo é a quantidade inteira — o que toda
/// adjudicação anterior significa. Preenchido, só faz sentido com <paramref name="QuotationItemId"/>.
/// </param>
public record AwardInput(string Family, Guid ProposalId, string? Criteria, string Justification,
    Guid? QuotationItemId = null, decimal? Quantity = null);

/// <summary>
/// Processo fechado de compras (RFQ-001): PR aprovada → cotação → propostas →
/// seleção justificada → aprovação Gerente → aprovação Diretor → emissão da OC.
/// Nenhuma etapa crítica pode ser pulada; toda transição gera evento imutável.
/// </summary>
public partial class QuotationService(AppDbContext db, TimeProvider clock)
{
    /// <summary>Suprimentos: conduz o processo (abre, convida, analisa, seleciona, emite).</summary>
    public static bool CanConduct(string role) =>
        role is Roles.PurchasingOfficer or Roles.SupplyManager or Roles.SystemAdministrator;

    /// <summary>
    /// Aprovação gerencial (1ª alçada). Aprovador (Pleno) só decide processos dos CCs que gerencia.
    /// O comprador também dá o Nível 1, em qualquer centro — decisão da empresa (2026-09): no
    /// formato dela, quem cota fecha a primeira alçada, e a segregação fica no Nível 2.
    /// </summary>
    public static bool CanApproveAsManager(string role) =>
        role is Roles.SupplyManager or Roles.SystemAdministrator or Roles.Approver or Roles.PurchasingOfficer;

    /// <summary>Aprovação da diretoria (2ª alçada).</summary>
    public static bool CanApproveAsDirector(string role) =>
        role is Roles.Director or Roles.SystemAdministrator;

    public static bool CanView(string role) =>
        CanConduct(role) || CanApproveAsManager(role) || CanApproveAsDirector(role) || role == Roles.Auditor;

    // ---- consulta -----------------------------------------------------------

    /// <summary>Página máxima aceita: evita que um `tamanho` grande vire consulta sem teto.</summary>
    public const int TamanhoMaximoDePagina = 500;

    /// <summary>
    /// Página de processos, com busca e filtro de situação feitos no banco. O
    /// `total` volta junto para a tela dizer quantos existem, e não só quantos
    /// vieram: buscar sobre uma lista truncada responde "nada encontrado" para
    /// processo que existe (PO-BR-012).
    /// </summary>
    public async Task<(List<Quotation> itens, int total)> ListAsync(
        string? busca = null, QuotationStatus? situacao = null,
        int tamanho = 100, CancellationToken ct = default)
    {
        var termo = busca?.Trim();
        var query = db.Quotations.AsQueryable();

        if (situacao is { } s) query = query.Where(q => q.Status == s);
        if (!string.IsNullOrEmpty(termo))
            query = query.Where(q =>
                EF.Functions.ILike(q.Number, $"%{termo}%")
                || EF.Functions.ILike(q.CostCenter, $"%{termo}%")
                || EF.Functions.ILike(q.SourcePrNumber, $"%{termo}%")
                // a SC de origem também vem por item, na cotação que juntou várias
                || q.Items.Any(i => EF.Functions.ILike(i.Description, $"%{termo}%")
                    || (i.SourcePrNumber != null && EF.Functions.ILike(i.SourcePrNumber, $"%{termo}%")))
                || q.Suppliers.Any(f => EF.Functions.ILike(f.SupplierName, $"%{termo}%")));

        var total = await query.CountAsync(ct);
        var itens = await query
            .Include(q => q.Items).Include(q => q.Suppliers).Include(q => q.Awards)
            .Include(q => q.Proposals).ThenInclude(p => p.Items)
            .OrderByDescending(q => q.CreatedAt)
            .Take(Math.Clamp(tamanho, 1, TamanhoMaximoDePagina)).ToListAsync(ct);
        return (itens, total);
    }

    /// <summary>
    /// Processos aguardando a alçada de quem está pedindo — <b>só os que ela pode decidir</b>,
    /// pela mesma régua da decisão (<c>ImpedimentoNivel1Async</c>/<c>ImpedimentoNivel2Async</c>):
    /// a lista do nível no centro, o gerente do centro sem lista, o diretor vinculado e a
    /// segregação. Uma fila com o que a pessoa não pode aprovar é uma fila que mente; quem
    /// quer ver a compra da empresa inteira tem o painel. O primeiro corte é no banco, e não
    /// sobre uma lista já truncada, para não esconder trabalho de quem decide (PO-BR-012).
    /// </summary>
    public async Task<List<Quotation>> PendingApprovalsAsync(
        string role, Guid actorId, CancellationToken ct = default)
    {
        var podeNivel1 = CanApproveAsManager(role);
        var podeNivel2 = CanApproveAsDirector(role);
        if (!podeNivel1 && !podeNivel2) return [];

        var candidatos = await db.Quotations.Where(q =>
                (podeNivel1 && q.Status == QuotationStatus.AwaitingManager)
                || (podeNivel2 && q.Status == QuotationStatus.AwaitingDirector))
            .Include(q => q.Items).Include(q => q.Suppliers).Include(q => q.Awards)
            .Include(q => q.Proposals).ThenInclude(p => p.Items)
            .OrderBy(q => q.CreatedAt)   // o mais antigo primeiro: a fila é de trabalho, não de novidade
            .ToListAsync(ct);

        var actor = new Actor(actorId, string.Empty, role);
        var fila = new List<Quotation>();
        foreach (var q in candidatos)
        {
            var impedimento = q.Status == QuotationStatus.AwaitingManager
                ? await ImpedimentoNivel1Async(q, actor, ct)
                : await ImpedimentoNivel2Async(q, actor, ct);
            if (impedimento is null) fila.Add(q);
        }
        return fila;
    }

    /// <summary>
    /// As decisões de alçada que esta pessoa tomou nos últimos dias, mais recentes primeiro.
    /// Sai dos eventos do processo — o mesmo registro que a auditoria lê —, não de uma lista
    /// paralela que poderia contar diferente.
    /// </summary>
    public async Task<List<DecisaoRecente>> MinhasDecisoesAsync(Guid actorId, int dias = 30, CancellationToken ct = default)
    {
        string[] tipos = ["GERENTE_APROVOU", "DIRETOR_APROVOU", "PROCESSO_REJEITADO", "AJUSTES_SOLICITADOS"];
        var desde = clock.GetUtcNow().AddDays(-dias);
        var eventos = await db.ProcessEvents
            .Where(e => e.ActorId == actorId && tipos.Contains(e.EventType) && e.OccurredAt >= desde)
            .OrderByDescending(e => e.OccurredAt).Take(50).ToListAsync(ct);
        if (eventos.Count == 0) return [];
        var ids = eventos.Select(e => e.QuotationId).Distinct().ToList();
        var processos = await db.Quotations.Include(q => q.Awards).Include(q => q.Proposals)
            .Where(q => ids.Contains(q.Id)).ToDictionaryAsync(q => q.Id, ct);
        return eventos.Where(e => processos.ContainsKey(e.QuotationId)).Select(e =>
        {
            var q = processos[e.QuotationId];
            var vencedora = q.Proposals.FirstOrDefault(p => p.Id == q.WinnerProposalId);
            var fornecedor = q.IsSplitAward ? string.Join(", ", q.AwardList.Select(a => a.SupplierName).Distinct())
                : q.AwardList.FirstOrDefault()?.SupplierName ?? vencedora?.SupplierName;
            var total = q.AwardList.Count > 0 ? q.AwardList.Sum(a => a.TotalValue) : vencedora?.TotalValue;
            return new DecisaoRecente(q.Id, q.Number, q.Status, e.EventType, e.OccurredAt, e.Note, fornecedor, total);
        }).ToList();
    }

    public Task<Quotation?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Quotations.Include(q => q.Items).Include(q => q.Suppliers).Include(q => q.Awards)
            .Include(q => q.Proposals).ThenInclude(p => p.Items)
            .SingleOrDefaultAsync(q => q.Id == id, ct);

    public Task<List<ProcessEvent>> TimelineAsync(Guid quotationId, CancellationToken ct = default) =>
        db.ProcessEvents.Where(e => e.QuotationId == quotationId)
            .OrderBy(e => e.OccurredAt).Take(500).ToListAsync(ct);

    /// <summary>Fila de Suprimentos: SCs com itens ainda sem processo de cotação.</summary>
    public async Task<List<QueueEntry>> AwaitingQuotationAsync(CancellationToken ct = default)
    {
        var (ready, _) = await QueueAsync(ct);
        return ready;
    }

    /// <summary>Teto da fila. Corta a fila <em>já filtrada</em> — 500 SCs de fato pendentes.</summary>
    private const int TetoDaFila = 500;

    /// <summary>
    /// Fila de Suprimentos completa: as SCs prontas para cotar — item a item, porque uma SC pode
    /// ter parte dos itens já em processo — e as que já têm comprador designado mas continuam
    /// retidas na aprovação, para o comprador enxergar o que está a caminho.
    ///
    /// <para>
    /// <b>O corte vem depois do filtro, e essa ordem é a correção.</b> Antes, a consulta pegava
    /// as 200 SCs abertas mais antigas e <em>só então</em> descartava as que já estavam em
    /// processo. Como toda SC atendida continua "aberta" até o fim do fluxo, as antigas já
    /// resolvidas ocupavam as 200 vagas e a fila devolvia 148 de 215 — a demanda <em>recente</em>,
    /// que é a que precisa de comprador, nunca chegava à tela. Agora o banco descarta o que já
    /// tem processo, O.C. direta ou nenhum item pendente, e o teto se aplica ao que sobrou.
    /// </para>
    /// </summary>
    public async Task<(List<QueueEntry> ready, List<QueueBlocked> blocked)> QueueAsync(
        CancellationToken ct = default)
    {
        var ativas = db.Quotations
            .Where(q => q.Status != QuotationStatus.Cancelled && q.Status != QuotationStatus.Rejected);

        var open = await db.Requisitions.Include(r => r.Items)
            .Where(r => r.DeletedAt == null
                        && (r.Status == RequisitionStatus.Submitted || r.Status == RequisitionStatus.Approved
                            || r.Status == RequisitionStatus.InApproval))
            // processos do fluxo antigo (sem rastreio por item) levam a SC inteira
            .Where(r => !ativas.Any(q => q.SourcePrId == r.Id && q.Items.All(i => i.SourcePrItemId == null)))
            // O.C. emitida fora do processo de cotação (rota de material) também encerra a SC
            .Where(r => !db.PurchaseOrders.Any(o => o.SourcePrId == r.Id && o.QuotationId == null
                                                    && o.Status != PurchaseOrderStatus.Cancelled))
            // e ao menos um item ainda fora de processo: SC com tudo cotado não é fila, é histórico
            .Where(r => r.Items.Any(i => !ativas.Any(q => q.Items.Any(qi => qi.SourcePrItemId == i.Id))))
            .OrderBy(r => r.DecidedAt ?? r.SubmittedAt).Take(TetoDaFila).ToListAsync(ct);

        // quais itens *destas* SCs já estão em processo — agora sobre o recorte carregado,
        // e não sobre a base inteira de itens de cotação
        var idsDeItens = open.SelectMany(r => r.Items).Select(i => i.Id).ToList();
        var itensEmProcesso = (await ativas.SelectMany(q => q.Items)
            .Where(i => i.SourcePrItemId != null && idsDeItens.Contains(i.SourcePrItemId!.Value))
            .Select(i => i.SourcePrItemId!.Value).ToListAsync(ct))
            .ToHashSet();

        var familias = await FamiliesOfAsync(open.SelectMany(r => r.Items).Select(i => i.CatalogItemId), ct);
        QueueEntry? Montar(PurchaseRequisition r)
        {
            var pendentes = r.Items.Where(i => !itensEmProcesso.Contains(i.Id)).OrderBy(i => i.Sequence)
                .Select(i => new QueueItem(i.Id, i.Sequence, i.CatalogCode, i.Description,
                    i.Quantity, i.UnitOfMeasure, i.EstimatedUnitPrice, FamilyOf(i, familias)))
                .ToList();
            return pendentes.Count == 0 ? null : new QueueEntry(r, pendentes, pendentes.Count < r.Items.Count);
        }

        var ready = open.Where(r => r.Status is RequisitionStatus.Submitted or RequisitionStatus.Approved)
            .Select(Montar).OfType<QueueEntry>().ToList();
        var waiting = open.Where(r => r.Status == RequisitionStatus.InApproval).ToList();   // legado
        if (waiting.Count == 0) return (ready, []);

        var codes = waiting.Select(r => r.CostCenter.ToUpperInvariant()).Distinct().ToList();
        var managers = await db.CostCenters.Where(c => c.Active && codes.Contains(c.Code.ToUpper()))
            .Select(c => new { c.Code, c.ManagerName }).ToListAsync(ct);
        var blocked = waiting.Select(r =>
        {
            var manager = managers.FirstOrDefault(m => m.Code.ToUpperInvariant() == r.CostCenter.ToUpperInvariant())?.ManagerName;
            return new QueueBlocked(r, string.IsNullOrWhiteSpace(manager)
                ? "Aguardando aprovação — nenhum gerente vinculado a este centro de custo."
                : $"Aguardando aprovação de {manager}.");
        }).ToList();
        return (ready, blocked);
    }

    // ---- infra --------------------------------------------------------------
    private async Task<(Quotation? q, UserError? error)> TransitionAsync(
        Actor actor, Guid id, QuotationStatus? requiredFrom, QuotationStatus to, string eventType,
        string description, string? note, CancellationToken ct, Func<Quotation, UserError?>? guard = null)
    {
        var q = await GetAsync(id, ct);
        if (q is null) return (null, new("RFQ-ERR-404", "Cotação não encontrada."));
        if (requiredFrom is not null && q.Status != requiredFrom)
            return (null, new("RFQ-ERR-020", "Transição não permitida no estado atual do processo."));
        if (guard?.Invoke(q) is { } error) return (null, error);
        var from = q.Status;
        q.Status = to;
        if (!string.IsNullOrWhiteSpace(note)) q.DecisionReason = note.Trim();
        AddEvent(q, eventType, description, actor, from, to, note);
        await TouchAndSaveAsync(q, ct);
        return (q, null);
    }

    private void AddEvent(Quotation q, string type, string description, Actor actor,
        QuotationStatus? from = null, QuotationStatus? to = null, string? note = null, Guid? documentId = null)
    {
        db.ProcessEvents.Add(new ProcessEvent
        {
            QuotationId = q.Id, EventType = type, Description = description,
            FromStatus = from, ToStatus = to,
            ActorId = actor.Id == Guid.Empty ? null : actor.Id, ActorLabel = actor.Label,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            DocumentId = documentId, OccurredAt = clock.GetUtcNow(),
        });
    }

    private async Task TouchAndSaveAsync(Quotation q, CancellationToken ct)
    {
        q.UpdatedAt = clock.GetUtcNow();
        q.Version += 1;
        await db.SaveChangesAsync(ct);
    }

    private async Task<long> NextSeqAsync(QuotationKind kind, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return await db.Quotations.LongCountAsync(q => (q.Kind == QuotationKind.Bid) == (kind == QuotationKind.Bid), ct) + 1;
        // dois literais em vez de um nome interpolado: nada aqui vem de fora, e
        // escrever assim tira o aviso do analisador em vez de suprimi-lo (SEC-D)
        return await db.Database.SqlQueryRaw<long>(kind == QuotationKind.Bid
                ? "SELECT nextval('procurement.bid_number_seq') AS \"Value\""
                : "SELECT nextval('procurement.rfq_number_seq') AS \"Value\"")
            .SingleAsync(ct);
    }

}
