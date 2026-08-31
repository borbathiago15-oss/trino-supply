using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

public record ProposalInput(
    int? DeliveryDays, string? PaymentTerms, decimal? FreightValue, DateOnly? ValidUntil,
    string? Notes, IReadOnlyList<ProposalItemInput> Items,
    decimal? DiscountValue = null, string? Currency = null, int? PaymentDays = null,
    decimal? TaxValue = null, decimal? OtherCosts = null);
/// <summary>SC já designada a um comprador, mas ainda retida na aprovação.</summary>
public record QueueBlocked(PurchaseRequisition Pr, string Reason);

public record ProposalItemInput(Guid QuotationItemId, decimal UnitPrice, decimal? Quantity);

/// <summary>Oferta de um fornecedor para uma família inteira (mapa de adjudicação).</summary>
public record FamilyOffer(Guid SupplierId, string SupplierName, Guid ProposalId, int ProposalVersion,
    decimal ItemsValue, decimal TotalValue, int? DeliveryDays, string? PaymentTerms,
    bool Complete, bool Cheapest);

/// <summary>Lote da compra: a família, o que ela pede e quem cotou.</summary>
public record FamilyLot(string Family, int ItemCount, decimal Quantity, IReadOnlyList<FamilyOffer> Offers);

/// <summary>
/// Escolha do fornecedor de UMA família (lote) da cotação. A mesma compra pode ter várias:
/// cada família com o seu vencedor, a sua justificativa e, na ponta, a sua O.C.
/// </summary>
public record AwardInput(string Family, Guid ProposalId, string? Criteria, string Justification);

/// <summary>
/// Processo fechado de compras (RFQ-001): PR aprovada → cotação → propostas →
/// seleção justificada → aprovação Gerente → aprovação Diretor → emissão da OC.
/// Nenhuma etapa crítica pode ser pulada; toda transição gera evento imutável.
/// </summary>
public class QuotationService(AppDbContext db, TimeProvider clock)
{
    /// <summary>Suprimentos: conduz o processo (abre, convida, analisa, seleciona, emite).</summary>
    public static bool CanConduct(string role) =>
        role is Roles.PurchasingOfficer or Roles.SupplyManager or Roles.SystemAdministrator;

    /// <summary>Aprovação gerencial (1ª alçada). Aprovador (Pleno) só decide processos dos CCs que gerencia.</summary>
    public static bool CanApproveAsManager(string role) =>
        role is Roles.SupplyManager or Roles.SystemAdministrator or Roles.Approver;

    /// <summary>Aprovação da diretoria (2ª alçada).</summary>
    public static bool CanApproveAsDirector(string role) =>
        role is Roles.Director or Roles.SystemAdministrator;

    public static bool CanView(string role) =>
        CanConduct(role) || CanApproveAsManager(role) || CanApproveAsDirector(role) || role == Roles.Auditor;

    // ---- consulta -----------------------------------------------------------
    public Task<List<Quotation>> ListAsync(CancellationToken ct = default) =>
        db.Quotations.Include(q => q.Items).Include(q => q.Suppliers).Include(q => q.Awards)
            .Include(q => q.Proposals).ThenInclude(p => p.Items)
            .OrderByDescending(q => q.CreatedAt).Take(200).ToListAsync(ct);

    public Task<Quotation?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Quotations.Include(q => q.Items).Include(q => q.Suppliers).Include(q => q.Awards)
            .Include(q => q.Proposals).ThenInclude(p => p.Items)
            .SingleOrDefaultAsync(q => q.Id == id, ct);

    public Task<List<ProcessEvent>> TimelineAsync(Guid quotationId, CancellationToken ct = default) =>
        db.ProcessEvents.Where(e => e.QuotationId == quotationId)
            .OrderBy(e => e.OccurredAt).Take(500).ToListAsync(ct);

    /// <summary>Fila de Suprimentos: PRs aprovadas sem cotação ativa e sem OC.</summary>
    public async Task<List<PurchaseRequisition>> AwaitingQuotationAsync(CancellationToken ct = default)
    {
        var (ready, _) = await QueueAsync(ct);
        return ready;
    }

    /// <summary>
    /// Fila de Suprimentos completa: as PRs prontas para cotar e as que já têm comprador designado
    /// mas continuam retidas na aprovação — o comprador precisa enxergar o que está a caminho.
    /// </summary>
    public async Task<(List<PurchaseRequisition> ready, List<QueueBlocked> blocked)> QueueAsync(
        CancellationToken ct = default)
    {
        var activeQuotes = db.Quotations
            .Where(q => q.Status != QuotationStatus.Cancelled && q.Status != QuotationStatus.Rejected);
        // multi-SC (V2): além da SC primária do cabeçalho, as SCs agrupadas via itens também saem da fila
        var activeQuotationPrs = (await activeQuotes.Select(q => q.SourcePrId).ToListAsync(ct))
            .Concat(await activeQuotes.SelectMany(q => q.Items)
                .Where(i => i.SourcePrId != null).Select(i => i.SourcePrId!.Value).ToListAsync(ct))
            .Distinct().ToList();
        var linkedPoPrs = await db.PurchaseOrders
            .Where(o => o.SourcePrId != null && o.Status != PurchaseOrderStatus.Cancelled)
            .Select(o => o.SourcePrId!.Value).ToListAsync(ct);

        var open = await db.Requisitions.Include(r => r.Items)
            .Where(r => r.DeletedAt == null
                        && (r.Status == RequisitionStatus.Submitted || r.Status == RequisitionStatus.Approved
                            || r.Status == RequisitionStatus.InApproval)
                        && !activeQuotationPrs.Contains(r.Id) && !linkedPoPrs.Contains(r.Id))
            .OrderBy(r => r.DecidedAt ?? r.SubmittedAt).Take(200).ToListAsync(ct);

        var ready = open.Where(r => r.Status is RequisitionStatus.Submitted or RequisitionStatus.Approved).ToList();
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

    // ---- abertura (RFQ-BR-001/002) ------------------------------------------
    public async Task<(Quotation? q, UserError? error)> CreateFromPrAsync(
        Actor actor, Guid prId, QuotationKind kind, DateOnly? deadline, string? notes, CancellationToken ct = default)
    {
        var pr = await db.Requisitions.Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == prId, ct);
        if (pr is null || pr.Status is not (RequisitionStatus.Submitted or RequisitionStatus.Approved))
            return (null, new("RFQ-ERR-001", "A requisição de origem precisa estar enviada (ou aprovada) para virar cotação."));
        if (await db.Quotations.AnyAsync(q =>
                (q.SourcePrId == prId || q.Items.Any(i => i.SourcePrId == prId))
                && q.Status != QuotationStatus.Cancelled && q.Status != QuotationStatus.Rejected, ct))
            return (null, new("RFQ-ERR-001", "A requisição já possui um processo de cotação ativo."));
        if (pr.Items.Count == 0)
            return (null, new("RFQ-ERR-001", "A requisição não possui itens."));

        var now = clock.GetUtcNow();
        var prefix = kind == QuotationKind.Bid ? "BID" : "RFQ";
        var seq = await NextSeqAsync(kind, ct);
        var q = new Quotation
        {
            Number = $"{prefix}-{now.Year}-{seq:000000}",
            Kind = kind,
            SourcePrId = pr.Id,
            SourcePrNumber = pr.Number,
            CostCenter = pr.CostCenter,
            Justification = pr.Justification,
            Deadline = deadline,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedBy = actor.Id,
            CreatedByLabel = actor.Label,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var familias = await FamiliesOfAsync(pr.Items.Select(i => i.CatalogItemId), ct);
        var seqNo = 0;
        foreach (var i in pr.Items.OrderBy(i => i.Sequence))
            q.Items.Add(new QuotationItem
            {
                QuotationId = q.Id, Sequence = ++seqNo, CatalogItemId = i.CatalogItemId,
                CatalogCode = i.CatalogCode, Description = i.Description,
                Quantity = i.Quantity, UnitOfMeasure = i.UnitOfMeasure, Family = FamilyOf(i.CatalogItemId, familias),
                SourcePrId = pr.Id, SourcePrNumber = pr.Number, SourcePrItemId = i.Id,
            });
        db.Quotations.Add(q);
        AddEvent(q, "COTACAO_ABERTA", $"Cotação {q.Number} aberta a partir da requisição {pr.Number}.",
            actor, null, QuotationStatus.Open);
        await db.SaveChangesAsync(ct);
        return (q, null);
    }

    // ---- abertura agrupada (V2 — regra 1 generalizada) -----------------------
    /// <summary>
    /// Abre um processo a partir de itens aprovados de UMA OU MAIS SCs do mesmo centro de custo.
    /// A SC mais antiga vira a primária do cabeçalho (compatibilidade); cada item guarda a origem.
    /// Alçadas preservadas: o centro de custo único mantém a semântica de aprovação por CC.
    /// </summary>
    public async Task<(Quotation? q, UserError? error)> CreateFromItemsAsync(
        Actor actor, IReadOnlyList<Guid> prItemIds, QuotationKind kind, DateOnly? deadline, string? notes,
        CancellationToken ct = default)
    {
        var ids = prItemIds.Distinct().ToList();
        if (ids.Count == 0)
            return (null, new("RFQ-ERR-060", "Selecione ao menos um item de solicitação para abrir o processo."));

        var prs = await db.Requisitions.Include(r => r.Items)
            .Where(r => r.DeletedAt == null && r.Items.Any(i => ids.Contains(i.Id)))
            .ToListAsync(ct);
        var found = prs.SelectMany(r => r.Items).Where(i => ids.Contains(i.Id)).Select(i => i.Id).ToHashSet();
        if (found.Count != ids.Count)
            return (null, new("RFQ-ERR-060", "Há item selecionado que não existe mais — atualize a lista."));

        foreach (var pr0 in prs)
            if (pr0.Status is not (RequisitionStatus.Submitted or RequisitionStatus.Approved))
                return (null, new("RFQ-ERR-060",
                    $"A solicitação {pr0.Number} precisa estar enviada (ou aprovada) para entrar em cotação."));

        var centros = prs.Select(r => r.CostCenter.Trim().ToUpperInvariant()).Distinct().ToList();
        if (centros.Count > 1)
            return (null, new("RFQ-ERR-061",
                "O agrupamento só aceita solicitações do MESMO centro de custo — as alçadas de aprovação são do centro. " +
                $"Selecionados: {string.Join(", ", centros)}."));

        // nenhum item pode estar em outro processo ativo (item a item, não mais SC a SC)
        var emProcesso = await db.Quotations
            .Where(q => q.Status != QuotationStatus.Cancelled && q.Status != QuotationStatus.Rejected)
            .SelectMany(q => q.Items.Where(i => i.SourcePrItemId != null && ids.Contains(i.SourcePrItemId.Value))
                .Select(i => new { i.SourcePrNumber, q.Number }))
            .FirstOrDefaultAsync(ct);
        if (emProcesso is not null)
            return (null, new("RFQ-ERR-062",
                $"Item da solicitação {emProcesso.SourcePrNumber} já está no processo ativo {emProcesso.Number}."));
        // SC de fluxo antigo (sem rastreio por item) em processo ativo também bloqueia
        var prIds = prs.Select(r => r.Id).ToList();
        var scAtiva = await db.Quotations
            .Where(q => prIds.Contains(q.SourcePrId)
                        && q.Status != QuotationStatus.Cancelled && q.Status != QuotationStatus.Rejected)
            .Select(q => q.Number).FirstOrDefaultAsync(ct);
        if (scAtiva is not null)
            return (null, new("RFQ-ERR-062", $"Solicitação selecionada já possui o processo ativo {scAtiva}."));

        var now = clock.GetUtcNow();
        var ordered = prs.OrderBy(r => r.SubmittedAt ?? r.CreatedAt).ToList();
        var primary = ordered[0];
        var prefix = kind == QuotationKind.Bid ? "BID" : "RFQ";
        var seq = await NextSeqAsync(kind, ct);
        var numbers = ordered.Select(r => r.Number).ToList();
        var q = new Quotation
        {
            Number = $"{prefix}-{now.Year}-{seq:000000}",
            Kind = kind,
            SourcePrId = primary.Id,
            SourcePrNumber = primary.Number,
            CostCenter = primary.CostCenter,
            Justification = ordered.Count == 1 ? primary.Justification
                : $"Agrupamento de demandas: {string.Join(", ", numbers)}.",
            Deadline = deadline,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedBy = actor.Id,
            CreatedByLabel = actor.Label,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var familias = await FamiliesOfAsync(
            ordered.SelectMany(r => r.Items).Where(i => ids.Contains(i.Id)).Select(i => i.CatalogItemId), ct);
        var seqNo = 0;
        foreach (var pr0 in ordered)
            foreach (var i in pr0.Items.Where(i => ids.Contains(i.Id)).OrderBy(i => i.Sequence))
                q.Items.Add(new QuotationItem
                {
                    QuotationId = q.Id, Sequence = ++seqNo, CatalogItemId = i.CatalogItemId,
                    CatalogCode = i.CatalogCode, Description = i.Description,
                    Quantity = i.Quantity, UnitOfMeasure = i.UnitOfMeasure, Family = FamilyOf(i.CatalogItemId, familias),
                    SourcePrId = pr0.Id, SourcePrNumber = pr0.Number, SourcePrItemId = i.Id,
                });
        db.Quotations.Add(q);
        AddEvent(q, "COTACAO_ABERTA", ordered.Count == 1
                ? $"Cotação {q.Number} aberta a partir da requisição {primary.Number}."
                : $"Cotação {q.Number} aberta agrupando as requisições {string.Join(", ", numbers)} ({q.Items.Count} itens).",
            actor, null, QuotationStatus.Open);
        await db.SaveChangesAsync(ct);
        return (q, null);
    }

    // ---- fornecedores convidados (RFQ-BR-003) --------------------------------
    public async Task<(Quotation? q, UserError? error)> InviteSuppliersAsync(
        Actor actor, Guid id, IReadOnlyList<Guid> supplierIds, CancellationToken ct = default)
    {
        var q = await GetAsync(id, ct);
        if (q is null) return (null, new("RFQ-ERR-404", "Cotação não encontrada."));
        // o comprador ainda inclui fornecedores durante a análise, enquanto não escolheu o vencedor
        if (q.Status is not (QuotationStatus.Open or QuotationStatus.Analysis) || q.WinnerSupplierId is not null)
            return (null, new("RFQ-ERR-020", "Só dá para convidar fornecedores enquanto o processo está em cotação/análise."));

        var suppliers = await db.Suppliers.Where(s => supplierIds.Contains(s.Id)).ToListAsync(ct);
        foreach (var sid in supplierIds.Distinct())
        {
            var s = suppliers.SingleOrDefault(x => x.Id == sid);
            if (s is null || !s.Active || s.HomologationStatus == SupplierHomologation.Bloqueado)
                return (null, new("RFQ-ERR-010", "Fornecedor inativo, bloqueado ou inexistente não pode ser convidado."));
            if (q.Suppliers.Any(x => x.SupplierId == sid)) continue;
            var invite = new QuotationSupplier
            {
                QuotationId = q.Id, SupplierId = s.Id,
                SupplierName = s.TradeName ?? s.LegalName, TaxId = s.TaxId,
                InvitedBy = actor.Id, InvitedByLabel = actor.Label, InvitedAt = clock.GetUtcNow(),
            };
            // Add explícito (chave pré-gerada em pai já rastreado); o EF liga o convite à cotação
            // sozinho — adicionar à coleção aqui deixava o fornecedor duplicado na resposta
            db.QuotationSuppliers.Add(invite);
            AddEvent(q, "FORNECEDOR_CONVIDADO", $"Fornecedor {s.TradeName ?? s.LegalName} convidado.", actor);
        }
        await TouchAndSaveAsync(q, ct);
        return (q, null);
    }

    // ---- propostas (RFQ-BR-004) ---------------------------------------------
    public async Task<(Proposal? proposal, UserError? error)> SubmitProposalAsync(
        Guid quotationId, Guid supplierId, ProposalInput input, string via, string submittedByLabel,
        CancellationToken ct = default)
    {
        var q = await GetAsync(quotationId, ct);
        if (q is null) return (null, new("RFQ-ERR-404", "Cotação não encontrada."));
        if (q.Status is not (QuotationStatus.Open or QuotationStatus.Analysis) || q.WinnerSupplierId is not null)
            return (null, new("RFQ-ERR-020", "A cotação não recebe mais propostas."));
        if (q.Suppliers.All(s => s.SupplierId != supplierId))
            return (null, new("RFQ-ERR-050", "Fornecedor não convidado para esta cotação."));
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (q.Deadline is not null && today > q.Deadline)
            return (null, new("RFQ-ERR-020", "O prazo de resposta desta cotação já encerrou."));
        if (input.Items.Count == 0 || input.Items.Any(i => i.UnitPrice < 0))
            return (null, new("RFQ-ERR-021", "Informe o preço de todos os itens (valores não negativos)."));
        var itemIds = q.Items.Select(i => i.Id).ToHashSet();
        if (input.Items.Any(i => !itemIds.Contains(i.QuotationItemId)))
            return (null, new("RFQ-ERR-021", "Item da proposta não pertence à cotação."));

        var supplier = await db.Suppliers.SingleAsync(s => s.Id == supplierId, ct);
        var version = q.Proposals.Where(p => p.SupplierId == supplierId)
            .Select(p => p.VersionNumber).DefaultIfEmpty(0).Max() + 1;
        var proposal = new Proposal
        {
            QuotationId = q.Id, SupplierId = supplierId,
            SupplierName = supplier.TradeName ?? supplier.LegalName,
            VersionNumber = version,
            DeliveryDays = input.DeliveryDays,
            PaymentTerms = string.IsNullOrWhiteSpace(input.PaymentTerms) ? null : input.PaymentTerms.Trim(),
            PaymentDays = input.PaymentDays,
            FreightValue = input.FreightValue,
            TaxValue = input.TaxValue,
            OtherCosts = input.OtherCosts,
            DiscountValue = input.DiscountValue,
            Currency = string.IsNullOrWhiteSpace(input.Currency) ? "BRL" : input.Currency.Trim().ToUpperInvariant(),
            ValidUntil = input.ValidUntil,
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            SubmittedVia = via,
            SubmittedByLabel = submittedByLabel,
            SubmittedAt = clock.GetUtcNow(),
        };
        foreach (var i in input.Items)
        {
            var qi = q.Items.Single(x => x.Id == i.QuotationItemId);
            proposal.Items.Add(new ProposalItem
            {
                ProposalId = proposal.Id, QuotationItemId = qi.Id,
                UnitPrice = i.UnitPrice, Quantity = i.Quantity ?? qi.Quantity,
            });
        }
        // custo total (V2-P2): itens + frete + impostos + outros − desconto
        proposal.TotalValue = proposal.Items.Sum(i => i.UnitPrice * i.Quantity)
            + (proposal.FreightValue ?? 0) + (proposal.TaxValue ?? 0) + (proposal.OtherCosts ?? 0)
            - (proposal.DiscountValue ?? 0);
        if (proposal.TotalValue < 0)
            return (null, new("RFQ-ERR-021", "O desconto não pode ser maior que o total da proposta."));
        db.Proposals.Add(proposal);
        AddEvent(q, "PROPOSTA_RECEBIDA",
            $"Proposta v{version} de {proposal.SupplierName} recebida ({via}) — total {proposal.TotalValue:0.00}.",
            new Actor(Guid.Empty, submittedByLabel, "Supplier"));
        await TouchAndSaveAsync(q, ct);
        return (proposal, null);
    }

    // ---- negociação e ganho (saving) ----------------------------------------
    /// <summary>
    /// Registra o resultado da negociação com um fornecedor: o comprador informa o valor
    /// fechado (ou o desconto em %) e o sistema grava uma nova versão da proposta e o ganho
    /// em relação à primeira proposta daquele fornecedor.
    /// </summary>
    public async Task<(Proposal? proposal, UserError? error)> RegisterNegotiationAsync(
        Actor actor, Guid id, Guid supplierId, decimal? closedValue, decimal? discountPercent,
        string? notes, CancellationToken ct = default)
    {
        var q = await GetAsync(id, ct);
        if (q is null) return (null, new("RFQ-ERR-404", "Cotação não encontrada."));
        if (q.Status is not (QuotationStatus.Open or QuotationStatus.Analysis) || q.WinnerSupplierId is not null)
            return (null, new("RFQ-ERR-020", "A negociação é registrada antes da escolha do fornecedor."));

        var versoes = q.Proposals.Where(p => p.SupplierId == supplierId).OrderBy(p => p.VersionNumber).ToList();
        if (versoes.Count == 0)
            return (null, new("RFQ-ERR-021", "Registre a proposta deste fornecedor antes de negociar."));
        var atual = versoes[^1];

        var fechado = closedValue;
        if (fechado is null)
        {
            if (discountPercent is null or <= 0)
                return (null, new("RFQ-ERR-022", "Informe o valor fechado ou o desconto negociado (%)."));
            if (discountPercent >= 100)
                return (null, new("RFQ-ERR-022", "O desconto negociado precisa ser menor que 100%."));
            fechado = Math.Round(atual.TotalValue * (1 - discountPercent.Value / 100m), 2);
        }
        if (fechado <= 0)
            return (null, new("RFQ-ERR-022", "O valor fechado precisa ser maior que zero."));
        if (fechado > atual.TotalValue)
            return (null, new("RFQ-ERR-022",
                $"O valor fechado ({fechado:0.00}) é maior que a proposta atual ({atual.TotalValue:0.00}): isso não é um ganho de negociação."));

        // a nova versão repete itens e frete e concentra a negociação no desconto
        var bruto = atual.Items.Sum(i => i.UnitPrice * i.Quantity) + (atual.FreightValue ?? 0)
            + (atual.TaxValue ?? 0) + (atual.OtherCosts ?? 0);
        var input = new ProposalInput(
            atual.DeliveryDays, atual.PaymentTerms, atual.FreightValue, atual.ValidUntil,
            string.IsNullOrWhiteSpace(notes) ? atual.Notes : notes.Trim(),
            atual.Items.Select(i => new ProposalItemInput(i.QuotationItemId, i.UnitPrice, i.Quantity)).ToList(),
            bruto - fechado.Value, atual.Currency, atual.PaymentDays, atual.TaxValue, atual.OtherCosts);
        var (nova, error) = await SubmitProposalAsync(q.Id, supplierId, input, "NEGOCIACAO", actor.Label, ct);
        if (error is not null) return (null, error);

        var primeira = versoes[0].TotalValue;
        q.NegotiationNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        q.NegotiatedByLabel = actor.Label;
        q.NegotiatedAt = clock.GetUtcNow();
        ApplySaving(q, nova!);
        AddEvent(q, "NEGOCIACAO_REGISTRADA",
            $"Negociação com {nova!.SupplierName}: de {primeira:0.00} para {nova.TotalValue:0.00} " +
            $"(ganho de {primeira - nova.TotalValue:0.00}).", actor, null, null, q.NegotiationNotes);
        await TouchAndSaveAsync(q, ct);
        return (nova, null);
    }

    /// <summary>Ganho da negociação: primeira proposta do fornecedor menos o valor fechado com ele.</summary>
    private void ApplySaving(Quotation q, Proposal fechada)
    {
        var primeira = q.Proposals.Where(p => p.SupplierId == fechada.SupplierId)
            .OrderBy(p => p.VersionNumber).First().TotalValue;
        q.BaselineValue = primeira;
        q.NegotiatedValue = fechada.TotalValue;
        q.SavingValue = primeira - fechada.TotalValue;
        q.SavingPercent = primeira > 0 ? Math.Round((primeira - fechada.TotalValue) / primeira * 100m, 2) : 0m;
    }

    // ---- encerramento p/ análise --------------------------------------------
    public Task<(Quotation? q, UserError? error)> CloseForAnalysisAsync(Actor actor, Guid id, CancellationToken ct = default) =>
        TransitionAsync(actor, id, QuotationStatus.Open, QuotationStatus.Analysis, "COTACAO_ENCERRADA",
            "Cotação encerrada para análise das propostas.", null, ct,
            guard: q => q.Proposals.Count == 0 ? new("RFQ-ERR-021", "Não há propostas recebidas para analisar.") : null);

    // ---- escolha do fornecedor (RFQ-BR-005) ----------------------------------
    /// <summary>
    /// Vencedor único: adjudica TODAS as famílias da cotação ao mesmo fornecedor.
    /// Continua sendo o caminho da compra que não se divide.
    /// </summary>
    public Task<(Quotation? q, UserError? error)> SelectWinnerAsync(
        Actor actor, Guid id, Guid proposalId, string? criteria, string? justification, CancellationToken ct = default) =>
        AwardAsync(actor, id, [new AwardInput(string.Empty, proposalId, criteria, justification ?? string.Empty)],
            todasAsFamilias: true, ct);

    /// <summary>
    /// Adjudicação por família (multi-fornecedor): a mesma compra vai para vários fornecedores,
    /// um por família, cada um com a sua justificativa. Toda família cotada precisa de vencedor.
    /// </summary>
    public Task<(Quotation? q, UserError? error)> AwardByFamilyAsync(
        Actor actor, Guid id, IReadOnlyList<AwardInput> awards, CancellationToken ct = default) =>
        AwardAsync(actor, id, awards, todasAsFamilias: false, ct);

    private async Task<(Quotation? q, UserError? error)> AwardAsync(
        Actor actor, Guid id, IReadOnlyList<AwardInput> pedidos, bool todasAsFamilias, CancellationToken ct)
    {
        var q = await GetAsync(id, ct);
        if (q is null) return (null, new("RFQ-ERR-404", "Cotação não encontrada."));
        if (q.Status != QuotationStatus.Analysis)
            return (null, new("RFQ-ERR-020", "A escolha do fornecedor acontece na etapa de análise."));
        if (pedidos.Count == 0)
            return (null, new("RFQ-ERR-021", "Informe o fornecedor vencedor de cada família."));

        var familias = q.Families;
        // vencedor único: a mesma escolha vale para todas as famílias do processo
        if (todasAsFamilias)
            pedidos = familias.Select(f => pedidos[0] with { Family = f }).ToList();

        var chaves = pedidos.Select(a => QuotationAward.FamilyKey(a.Family)).ToList();
        if (chaves.Distinct().Count() != chaves.Count)
            return (null, new("RFQ-ERR-023", "Cada família só pode ser adjudicada uma vez."));
        if (chaves.FirstOrDefault(f => !familias.Contains(f)) is { } intrusa)
            return (null, new("RFQ-ERR-023", $"A família {intrusa} não faz parte desta cotação."));
        var faltando = familias.Where(f => !chaves.Contains(f)).ToList();
        if (faltando.Count > 0)
            return (null, new("RFQ-ERR-023",
                $"Falta escolher o fornecedor da(s) família(s) {string.Join(", ", faltando)} — toda família cotada precisa de um vencedor."));

        var now = clock.GetUtcNow();
        var hoje = DateOnly.FromDateTime(now.UtcDateTime);
        var idsFornecedores = q.Proposals.Where(p => pedidos.Any(x => x.ProposalId == p.Id))
            .Select(p => p.SupplierId).Distinct().ToList();
        var fornecedores = await db.Suppliers.Include(f => f.Documents)
            .Where(f => idsFornecedores.Contains(f.Id)).ToListAsync(ct);

        // valida tudo antes de gravar: escolha parcial não pode deixar o processo pela metade
        var novas = new List<QuotationAward>();
        foreach (var pedido in pedidos)
        {
            var familia = QuotationAward.FamilyKey(pedido.Family);
            if (string.IsNullOrWhiteSpace(pedido.Justification))
                return (null, new("RFQ-ERR-021", familias.Count > 1
                    ? $"A justificativa da escolha é obrigatória (família {familia})."
                    : "A justificativa da escolha é obrigatória."));
            var proposal = q.Proposals.SingleOrDefault(p => p.Id == pedido.ProposalId);
            if (proposal is null) return (null, new("RFQ-ERR-021", "Proposta vencedora inexistente nesta cotação."));
            var latest = q.Proposals.Where(p => p.SupplierId == proposal.SupplierId)
                .OrderByDescending(p => p.VersionNumber).First();
            if (latest.Id != proposal.Id)
                return (null, new("RFQ-ERR-021", "Selecione a versão mais recente da proposta do fornecedor."));

            var itens = ItemsOfFamily(q, familia);
            if (itens.Any(i => proposal.Items.All(pi => pi.QuotationItemId != i)))
                return (null, new("RFQ-ERR-024",
                    $"{proposal.SupplierName} não cotou todos os itens da família {familia} — escolha um fornecedor que tenha cotado a família inteira."));

            // homologação (V2-P2): prospect participa da cotação, mas só homologado é selecionado
            var vencedor = fornecedores.SingleOrDefault(f => f.Id == proposal.SupplierId)
                ?? await db.Suppliers.Include(f => f.Documents).SingleAsync(f => f.Id == proposal.SupplierId, ct);
            var situacao = vencedor.EffectiveHomologation(hoje);
            if (situacao != SupplierHomologation.Homologado)
                return (null, new("SUP-ERR-030",
                    $"O fornecedor {proposal.SupplierName} está {situacao} — conclua a homologação (ou regularize as certidões) antes de selecioná-lo."));

            var (valorItens, total) = ShareOf(proposal, itens);
            novas.Add(new QuotationAward
            {
                QuotationId = q.Id, Family = familia,
                SupplierId = proposal.SupplierId, SupplierName = proposal.SupplierName,
                ProposalId = proposal.Id, ProposalVersion = proposal.VersionNumber,
                ItemsValue = valorItens, TotalValue = total,
                Criteria = string.IsNullOrWhiteSpace(pedido.Criteria) ? null : pedido.Criteria.Trim(),
                Justification = pedido.Justification.Trim(),
                SelectedBy = actor.Id, SelectedByLabel = actor.Label, SelectedAt = now,
            });
        }

        // reescolha depois de "solicitar ajustes": a adjudicação anterior é substituída inteira
        foreach (var antiga in q.AwardList) db.QuotationAwards.Remove(antiga);
        q.Awards.Clear();
        foreach (var nova in novas) { db.QuotationAwards.Add(nova); q.Awards.Add(nova); }

        // cabeçalho: continua apontando o fornecedor de maior fatia (compatibilidade das telas e alçadas)
        var principal = novas.OrderByDescending(a => a.TotalValue).First();
        var distintos = novas.Select(a => a.SupplierId).Distinct().Count();
        var totalGeral = novas.Sum(a => a.TotalValue);
        q.WinnerSupplierId = principal.SupplierId;
        q.WinnerProposalId = principal.ProposalId;
        var criterios = novas.Select(a => a.Criteria).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        q.SelectionCriteria = criterios.Count == 0 ? null : string.Join(", ", criterios);
        q.SelectionJustification = distintos == 1 && novas.Select(a => a.Justification).Distinct().Count() == 1
            ? principal.Justification
            : string.Join(" | ", novas.Select(a => $"{a.Family}: {a.Justification}"));
        q.SelectedBy = actor.Id;
        q.SelectedByLabel = actor.Label;
        q.SelectedAt = now;
        ApplyAwardSaving(q, novas);

        var from = q.Status;
        q.Status = QuotationStatus.AwaitingManager;
        AddEvent(q, distintos == 1 ? "FORNECEDOR_SELECIONADO" : "COMPRA_DIVIDIDA",
            distintos == 1
                ? $"Fornecedor {principal.SupplierName} selecionado (proposta v{principal.ProposalVersion}, total {totalGeral:0.00}). Processo encaminhado à aprovação gerencial."
                : $"Compra dividida entre {distintos} fornecedores por família — " +
                  string.Join("; ", novas.Select(a => $"{a.Family} → {a.SupplierName} ({a.TotalValue:0.00})")) +
                  $". Total {totalGeral:0.00}. Processo encaminhado à aprovação gerencial.",
            actor, from, q.Status, q.SelectionJustification);
        await TouchAndSaveAsync(q, ct);
        return (q, null);
    }

    /// <summary>
    /// Mapa de adjudicação: para cada família, quanto sai com cada fornecedor. Só quem cotou a
    /// família inteira pode levá-la; quem cotou parte aparece marcado como incompleto.
    /// </summary>
    public IReadOnlyList<FamilyLot> FamilyMap(Quotation q)
    {
        var atuais = q.Proposals.GroupBy(p => p.SupplierId)
            .Select(g => g.OrderByDescending(p => p.VersionNumber).First()).ToList();
        var lotes = new List<FamilyLot>();
        foreach (var familia in q.Families)
        {
            var itens = ItemsOfFamily(q, familia);
            var ofertas = new List<FamilyOffer>();
            foreach (var p in atuais)
            {
                var cobre = itens.All(i => p.Items.Any(pi => pi.QuotationItemId == i));
                var (valorItens, total) = ShareOf(p, itens);
                if (!cobre && valorItens <= 0) continue;    // não cotou nada desta família
                ofertas.Add(new FamilyOffer(p.SupplierId, p.SupplierName, p.Id, p.VersionNumber,
                    valorItens, total, p.DeliveryDays, p.PaymentTerms, cobre, false));
            }
            var menor = ofertas.Where(o => o.Complete).OrderBy(o => o.TotalValue).FirstOrDefault();
            lotes.Add(new FamilyLot(familia, itens.Count,
                q.Items.Where(i => itens.Contains(i.Id)).Sum(i => i.Quantity),
                ofertas.Select(o => o with { Cheapest = menor is not null && o.Complete && o.TotalValue == menor.TotalValue })
                    .OrderByDescending(o => o.Complete).ThenBy(o => o.TotalValue).ToList()));
        }
        return lotes;
    }

    /// <summary>Itens de uma família dentro do processo (a família do item é snapshot do catálogo).</summary>
    private static List<Guid> ItemsOfFamily(Quotation q, string familia) =>
        q.Items.Where(i => QuotationAward.FamilyKey(i.Family) == familia).Select(i => i.Id).ToList();

    /// <summary>
    /// Fatia de uma proposta: os itens indicados pelo preço cotado mais o rateio proporcional de
    /// frete, impostos, outros custos e desconto. Fornecedor que leva tudo fica com o total cheio.
    /// </summary>
    private static (decimal items, decimal total) ShareOf(Proposal p, IReadOnlyCollection<Guid> quotationItemIds)
    {
        var cotado = p.Items.Sum(i => i.UnitPrice * i.Quantity);
        var fatia = p.Items.Where(i => quotationItemIds.Contains(i.QuotationItemId))
            .Sum(i => i.UnitPrice * i.Quantity);
        if (p.Items.All(i => quotationItemIds.Contains(i.QuotationItemId)))
            return (fatia, p.TotalValue);          // levou a proposta inteira: sem rateio, sem arredondamento
        var extras = (p.FreightValue ?? 0) + (p.TaxValue ?? 0) + (p.OtherCosts ?? 0) - (p.DiscountValue ?? 0);
        var proporcao = cotado > 0 ? fatia / cotado : 0m;
        return (fatia, Math.Round(fatia + extras * proporcao, 2));
    }

    /// <summary>Ganho agregado: primeira proposta de cada vencedor, na fatia que ele ganhou, menos o fechado.</summary>
    private void ApplyAwardSaving(Quotation q, IReadOnlyList<QuotationAward> awards)
    {
        decimal baseline = 0, fechado = 0;
        foreach (var a in awards)
        {
            var itens = ItemsOfFamily(q, a.Family);
            var primeira = q.Proposals.Where(p => p.SupplierId == a.SupplierId)
                .OrderBy(p => p.VersionNumber).First();
            baseline += ShareOf(primeira, itens).total;
            fechado += a.TotalValue;
        }
        q.BaselineValue = baseline;
        q.NegotiatedValue = fechado;
        q.SavingValue = baseline - fechado;
        q.SavingPercent = baseline > 0 ? Math.Round((baseline - fechado) / baseline * 100m, 2) : 0m;
    }

    /// <summary>Famílias do catálogo, em caixa alta; item digitado (sem catálogo) entra em DIVERSOS.</summary>
    private async Task<Dictionary<Guid, string>> FamiliesOfAsync(IEnumerable<Guid?> catalogItemIds, CancellationToken ct)
    {
        var ids = catalogItemIds.Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.CatalogItems.Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => QuotationAward.FamilyKey(c.Family), ct);
    }

    private static string FamilyOf(Guid? catalogItemId, IReadOnlyDictionary<Guid, string> familias) =>
        catalogItemId is { } id && familias.TryGetValue(id, out var f) ? f : QuotationAward.Default;

    // ---- alçadas (RFQ-BR-006/007) --------------------------------------------
    public async Task<(Quotation? q, UserError? error)> ManagerDecisionAsync(
        Actor actor, Guid id, string decision, string? reason, CancellationToken ct = default)
    {
        var q = await GetAsync(id, ct);
        if (q is null) return (null, new("RFQ-ERR-404", "Cotação não encontrada."));
        if (q.Status != QuotationStatus.AwaitingManager)
            return (null, new("RFQ-ERR-020", "O processo não está aguardando aprovação gerencial."));
        if (actor.Id == q.SelectedBy)
            return (null, new("RFQ-ERR-030", "Segregação de funções: quem selecionou o fornecedor não aprova a própria escolha."));
        // alçada do centro (Nível 1): qualquer pessoa da lista resolve a etapa
        if (actor.Role != Roles.SystemAdministrator &&
            await ApprovalLevels.CanDecideAsync(db, q.CostCenter, ApprovalLevels.Level1, actor.Id, ct) is { } noNivel1)
        {
            if (!noNivel1)
                return (null, new("RFQ-ERR-031", "Alçada por centro de custo: a 1ª aprovação deste processo é do Nível 1 do centro (" +
                    await ApprovalLevels.LabelAsync(db, q.CostCenter, ApprovalLevels.Level1, ct) + ")."));
        }
        else if (actor.Role == Roles.Approver)
        {
            // centro sem Nível 1 cadastrado: vale o gerente responsável antigo
            var cc = q.CostCenter.Trim().ToUpperInvariant();
            var manages = await db.CostCenters.AnyAsync(
                c => c.Active && c.ManagerUserId == actor.Id && c.Code == cc, ct);
            if (!manages)
                return (null, new("RFQ-ERR-031", "Alçada por centro de custo: este processo pertence a um centro de custo que não está sob a sua gerência."));
        }
        return await DecideAsync(q, actor, decision, reason, isDirector: false, ct);
    }

    public async Task<(Quotation? q, UserError? error)> DirectorDecisionAsync(
        Actor actor, Guid id, string decision, string? reason, CancellationToken ct = default)
    {
        var q = await GetAsync(id, ct);
        if (q is null) return (null, new("RFQ-ERR-404", "Cotação não encontrada."));
        if (q.Status != QuotationStatus.AwaitingDirector)
            return (null, new("RFQ-ERR-020", "O processo não está aguardando aprovação da diretoria."));
        if (actor.Id == q.SelectedBy || actor.Id == q.ManagerApprovedBy)
            return (null, new("RFQ-ERR-030", "Segregação de funções: o Diretor não pode ser quem selecionou nem quem deu a aprovação gerencial."));
        if (actor.Role != Roles.SystemAdministrator)
        {
            // alçada do centro (Nível 2): qualquer pessoa da lista resolve a etapa
            if (await ApprovalLevels.CanDecideAsync(db, q.CostCenter, ApprovalLevels.Level2, actor.Id, ct) is { } noNivel2)
            {
                if (!noNivel2)
                    return (null, new("RFQ-ERR-032", "Alçada por centro de custo: a 2ª aprovação deste processo é do Nível 2 do centro (" +
                        await ApprovalLevels.LabelAsync(db, q.CostCenter, ApprovalLevels.Level2, ct) + ")."));
            }
            // centro sem Nível 2 cadastrado: vale o diretor vinculado ao gerente
            else if (await LinkedDirectorAsync(q, ct) is { } linkedDirector && linkedDirector != actor.Id)
                return (null, new("RFQ-ERR-032", "Alçada por diretoria: este processo está vinculado a outro diretor responsável."));
        }
        return await DecideAsync(q, actor, decision, reason, isDirector: true, ct);
    }

    /// <summary>
    /// Diretor vinculado ao processo: o diretor cadastrado no gerente que aprovou a 1ª alçada
    /// (ou, na falta dele, no gerente do centro de custo). Null = qualquer diretor pode decidir.
    /// </summary>
    private async Task<Guid?> LinkedDirectorAsync(Quotation q, CancellationToken ct)
    {
        var managerId = q.ManagerApprovedBy;
        if (managerId is null)
        {
            var cc = q.CostCenter.Trim().ToUpperInvariant();
            managerId = await db.CostCenters.Where(c => c.Active && c.Code == cc)
                .Select(c => c.ManagerUserId).FirstOrDefaultAsync(ct);
        }
        if (managerId is null) return null;
        return await db.Users.Where(u => u.Id == managerId).Select(u => u.DirectorId).FirstOrDefaultAsync(ct);
    }

    /// <summary>Compra autorizada pela diretoria: TODAS as SCs de origem passam a APROVADAS (autorização com preço).</summary>
    private async Task MarkSourcePrApprovedAsync(Quotation q, Actor actor, CancellationToken ct)
    {
        var prIds = q.SourcePrIds;
        var prs = await db.Requisitions.Where(r => prIds.Contains(r.Id)).ToListAsync(ct);
        foreach (var pr in prs)
        {
            if (pr.Status == RequisitionStatus.Approved) continue;
            pr.Status = RequisitionStatus.Approved;
            pr.DecidedById = actor.Id;
            pr.DecidedByLabel = actor.Label;
            pr.DecidedAt = clock.GetUtcNow();
            pr.DecisionReason = $"Compra aprovada no processo {q.Number}.";
            pr.UpdatedAt = clock.GetUtcNow();
            pr.Version += 1;
        }
    }

    private async Task<(Quotation? q, UserError? error)> DecideAsync(
        Quotation q, Actor actor, string decision, string? reason, bool isDirector, CancellationToken ct)
    {
        decision = decision.Trim().ToUpperInvariant();
        var stage = isDirector ? "diretoria" : "gerencial";
        var from = q.Status;
        switch (decision)
        {
            case "APROVAR":
                if (isDirector)
                {
                    q.DirectorApprovedBy = actor.Id;
                    q.DirectorApprovedByLabel = actor.Label;
                    q.DirectorApprovedAt = clock.GetUtcNow();
                    q.Status = QuotationStatus.ApprovedForIssue;
                    await MarkSourcePrApprovedAsync(q, actor, ct);
                    AddEvent(q, "DIRETOR_APROVOU",
                        "Aprovador 02 (Nível 2) aprovou. Processo aguardando o comprador registrar a OC fechada no SENIOR.",
                        actor, from, q.Status, reason);
                }
                else
                {
                    q.ManagerApprovedBy = actor.Id;
                    q.ManagerApprovedByLabel = actor.Label;
                    q.ManagerApprovedAt = clock.GetUtcNow();
                    q.Status = QuotationStatus.AwaitingDirector;
                    AddEvent(q, "GERENTE_APROVOU",
                        "Aprovador 01 (Nível 1) aprovou. Processo encaminhado ao Nível 2.",
                        actor, from, q.Status, reason);
                }
                break;
            case "REJEITAR":
                if (string.IsNullOrWhiteSpace(reason))
                    return (null, new("RFQ-ERR-021", "A rejeição exige justificativa."));
                q.Status = QuotationStatus.Rejected;
                q.DecisionReason = reason.Trim();
                AddEvent(q, "PROCESSO_REJEITADO", $"Processo rejeitado na alçada {stage}.", actor, from, q.Status, reason.Trim());
                break;
            case "AJUSTES":
                if (string.IsNullOrWhiteSpace(reason))
                    return (null, new("RFQ-ERR-021", "A solicitação de ajustes exige justificativa."));
                q.Status = QuotationStatus.Analysis;   // volta para Suprimentos mantendo todo o histórico
                q.DecisionReason = reason.Trim();
                if (isDirector) { q.ManagerApprovedBy = null; q.ManagerApprovedByLabel = null; q.ManagerApprovedAt = null; }
                AddEvent(q, "AJUSTES_SOLICITADOS",
                    $"Alçada {stage} solicitou ajustes — processo devolvido a Suprimentos.", actor, from, q.Status, reason.Trim());
                break;
            default:
                return (null, new("RFQ-ERR-021", "Decisão inválida: use APROVAR, REJEITAR ou AJUSTES."));
        }
        await TouchAndSaveAsync(q, ct);
        return (q, null);
    }

    // ---- registro da OC fechada no ERP (SENIOR) -----------------------------
    /// <summary>
    /// A OC não é mais emitida aqui: ela é fechada no SENIOR (que conversa com o financeiro) e o
    /// comprador registra o número dela no processo, dando origem ao pedido que recebe o
    /// faturamento e a entrega (revisão de telas 2026-08-26).
    /// Compra dividida: uma O.C. POR FORNECEDOR — as famílias que o mesmo fornecedor ganhou
    /// entram na mesma O.C., com a família visível linha a linha. O processo só fecha quando
    /// todas as O.C.s estiverem registradas.
    /// </summary>
    public async Task<(PurchaseOrder? order, UserError? error)> RegisterErpPurchaseOrderAsync(
        Actor actor, Guid id, string? erpNumber, DateOnly? issuedOn, string? notes,
        string? overLimitJustification = null, Guid? supplierId = null, CancellationToken ct = default)
    {
        var q = await GetAsync(id, ct);
        if (q is null) return (null, new("RFQ-ERR-404", "Cotação não encontrada."));
        if (q.Status != QuotationStatus.ApprovedForIssue)
            return (null, new("RFQ-ERR-040", "A OC só é registrada depois das duas aprovações."));
        var numero = erpNumber?.Trim();
        if (string.IsNullOrWhiteSpace(numero))
            return (null, new("RFQ-ERR-041", "Informe o número da OC fechada no SENIOR."));
        if (numero.Length > 30) return (null, new("RFQ-ERR-041", "O número da OC tem no máximo 30 caracteres."));
        if (await db.PurchaseOrders.AnyAsync(o => o.Number == numero, ct))
            return (null, new("RFQ-ERR-041", $"A OC {numero} já está registrada em outro processo."));

        // processos anteriores à adjudicação por família continuam valendo: viram uma adjudicação única
        await EnsureAwardsAsync(q, ct);
        var pendentes = q.AwardList.Where(a => a.PurchaseOrderId is null).ToList();
        if (pendentes.Count == 0)
            return (null, new("RFQ-ERR-040", "Todas as O.C.s deste processo já foram registradas."));
        var aguardando = pendentes.Select(a => a.SupplierId).Distinct().ToList();
        Guid alvo;
        if (supplierId is { } escolhido)
        {
            if (!aguardando.Contains(escolhido))
                return (null, new("RFQ-ERR-042", "Este fornecedor não tem família pendente de O.C. neste processo."));
            alvo = escolhido;
        }
        else if (aguardando.Count == 1) alvo = aguardando[0];
        else return (null, new("RFQ-ERR-042",
            $"A compra foi dividida entre {aguardando.Count} fornecedores: informe de qual fornecedor é esta O.C."));

        var doFornecedor = pendentes.Where(a => a.SupplierId == alvo).ToList();
        var proposal = q.Proposals.Single(p => p.Id == doFornecedor[0].ProposalId);
        // itens do contrato incluídos: a vigência (ContractIsCurrent) depende deles para o teto
        var supplier = await db.Suppliers.Include(s => s.ContractItems)
            .SingleOrDefaultAsync(s => s.Id == alvo, ct);
        if (supplier is null || !supplier.Active)
            return (null, new("RFQ-ERR-040", "Fornecedor vencedor inativo: regularize o cadastro ou solicite ajustes."));

        var now = clock.GetUtcNow();
        var familias = doFornecedor.Select(a => a.Family).OrderBy(f => f).ToList();
        var itensDaOc = familias.SelectMany(f => ItemsOfFamily(q, f)).Distinct().ToHashSet();
        var order = new PurchaseOrder
        {
            Number = numero,
            ErpNumber = numero,
            ErpIssuedOn = issuedOn ?? DateOnly.FromDateTime(now.UtcDateTime),
            // congela a data prometida para o OTIF: data da O.C. + prazo de entrega da proposta
            PromisedDate = proposal.DeliveryDays is { } prazo
                ? (issuedOn ?? DateOnly.FromDateTime(now.UtcDateTime)).AddDays(prazo)
                : null,
            SupplierId = supplier.Id,
            SupplierName = supplier.TradeName ?? supplier.LegalName,
            SourcePrId = q.SourcePrId,
            SourcePrNumber = q.SourcePrNumber,
            QuotationId = q.Id,
            QuotationNumber = q.Number,
            Families = q.Families.Count > 1 ? string.Join(", ", familias) : null,
            PaymentTerms = proposal.PaymentTerms,
            DeliveryDays = proposal.DeliveryDays,
            FreightValue = FreightShare(proposal, itensDaOc),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            IssuedBy = actor.Id,
            IssuedByLabel = actor.Label,
            CreatedAt = now,
            UpdatedAt = now,
        };
        // saving de referência (V2-P2): último preço pago de cada item de catálogo, congelado agora
        var catalogIds = q.Items.Where(i => itensDaOc.Contains(i.Id) && i.CatalogItemId is not null)
            .Select(i => i.CatalogItemId!.Value).Distinct().ToList();
        var ultimosPrecos = catalogIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : (await db.PurchaseOrderItems
                .Where(i => i.CatalogItemId != null && catalogIds.Contains(i.CatalogItemId.Value)
                            && i.UnitPrice != null)
                .Join(db.PurchaseOrders.Where(o => o.Status != PurchaseOrderStatus.Cancelled),
                      i => i.OrderId, o => o.Id, (i, o) => new { i.CatalogItemId, i.UnitPrice, o.CreatedAt })
                .ToListAsync(ct))
              .GroupBy(x => x.CatalogItemId!.Value)
              .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).First().UnitPrice!.Value);

        foreach (var pi in proposal.Items.Where(pi => itensDaOc.Contains(pi.QuotationItemId)))
        {
            var qi = q.Items.Single(x => x.Id == pi.QuotationItemId);
            var ultimo = qi.CatalogItemId is not null && ultimosPrecos.TryGetValue(qi.CatalogItemId.Value, out var v)
                ? v : (decimal?)null;
            order.Items.Add(new PurchaseOrderItem
            {
                Description = qi.Description, UnitOfMeasure = qi.UnitOfMeasure,
                Quantity = pi.Quantity, UnitPrice = pi.UnitPrice,
                CatalogItemId = qi.CatalogItemId, CatalogCode = qi.CatalogCode,
                LastPaidUnitPrice = ultimo,
                ReferenceSaving = ultimo is not null ? (ultimo.Value - pi.UnitPrice) * pi.Quantity : null,
                SourcePrNumber = qi.SourcePrNumber ?? q.SourcePrNumber,
                Family = QuotationAward.FamilyKey(qi.Family),
                CreatedAt = now,
            });
        }
        // o valor da O.C. é a soma das fatias adjudicadas (itens + rateio de frete/impostos/desconto)
        order.TotalValue = doFornecedor.Sum(a => a.TotalValue);
        if (order.Items.Count == 0 || order.TotalValue <= 0)
            return (null, new("RFQ-ERR-040", "A OC precisa de itens e valor."));

        // teto do contrato de parceria (V2-P2): exceder exige justificativa — mede, não trava cego
        if (supplier.ContractValueLimit is { } teto && supplier.ContractIsCurrent(DateOnly.FromDateTime(now.UtcDateTime)))
        {
            var consumido = await ContractConsumedAsync(supplier, ct);
            if (consumido + order.TotalValue > teto && string.IsNullOrWhiteSpace(overLimitJustification))
                return (null, new("CT-ERR-010",
                    $"Esta O.C. ({order.TotalValue:0.00}) ultrapassa o saldo do contrato {supplier.ContractNumber} " +
                    $"(teto {teto:0.00}, consumido {consumido:0.00}). Informe a justificativa para prosseguir."));
            if (consumido + order.TotalValue > teto)
                AddEvent(q, "CONTRATO_TETO_EXCEDIDO",
                    $"O.C. {numero} excede o teto do contrato {supplier.ContractNumber} " +
                    $"({consumido + order.TotalValue:0.00} de {teto:0.00}).", actor, null, null, overLimitJustification!.Trim());
        }
        db.PurchaseOrders.Add(order);
        foreach (var a in doFornecedor) { a.PurchaseOrderId = order.Id; a.PurchaseOrderNumber = order.Number; }

        var from = q.Status;
        q.PurchaseOrderId ??= order.Id;              // a primeira O.C. mantém o vínculo histórico do cabeçalho
        q.PurchaseOrderNumber ??= order.Number;
        var restantes = q.AwardList.Where(a => a.PurchaseOrderId is null).Select(a => a.SupplierName)
            .Distinct().ToList();
        var lote = q.Families.Count > 1 ? $" (família(s) {string.Join(", ", familias)})" : "";
        if (restantes.Count == 0)
        {
            q.Status = QuotationStatus.PoIssued;
            AddEvent(q, "OC_REGISTRADA",
                $"OC {order.Number} do SENIOR registrada para {order.SupplierName}{lote} — total {order.TotalValue:0.00}." +
                (q.IsSplitAward ? " Todas as O.C.s da compra dividida estão registradas." : ""),
                actor, from, q.Status);
        }
        else
        {
            AddEvent(q, "OC_PARCIAL_REGISTRADA",
                $"OC {order.Number} do SENIOR registrada para {order.SupplierName}{lote} — total {order.TotalValue:0.00}. " +
                $"Falta registrar a O.C. de: {string.Join(", ", restantes)}.", actor);
        }
        await TouchAndSaveAsync(q, ct);
        return (order, null);
    }

    /// <summary>
    /// Cotação escolhida antes da adjudicação por família (ou pelo caminho de vencedor único legado):
    /// materializa uma adjudicação por família apontando para o vencedor do cabeçalho, para que o
    /// registro da O.C. tenha sempre a mesma origem.
    /// </summary>
    private async Task EnsureAwardsAsync(Quotation q, CancellationToken ct)
    {
        if (q.AwardList.Count > 0 || q.WinnerProposalId is null) return;
        var proposal = q.Proposals.Single(p => p.Id == q.WinnerProposalId);
        foreach (var familia in q.Families)
        {
            var (valorItens, total) = ShareOf(proposal, ItemsOfFamily(q, familia));
            var award = new QuotationAward
            {
                QuotationId = q.Id, Family = familia,
                SupplierId = proposal.SupplierId, SupplierName = proposal.SupplierName,
                ProposalId = proposal.Id, ProposalVersion = proposal.VersionNumber,
                ItemsValue = valorItens, TotalValue = total,
                Criteria = q.SelectionCriteria, Justification = q.SelectionJustification ?? string.Empty,
                SelectedBy = q.SelectedBy ?? Guid.Empty, SelectedByLabel = q.SelectedByLabel ?? string.Empty,
                SelectedAt = q.SelectedAt ?? clock.GetUtcNow(),
            };
            db.QuotationAwards.Add(award);
            q.Awards.Add(award);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Frete da fatia: proporcional ao valor dos itens que entram nesta O.C.</summary>
    private static decimal? FreightShare(Proposal p, IReadOnlyCollection<Guid> quotationItemIds)
    {
        if (p.FreightValue is not { } frete) return null;
        if (p.Items.All(i => quotationItemIds.Contains(i.QuotationItemId))) return frete;
        var cotado = p.Items.Sum(i => i.UnitPrice * i.Quantity);
        if (cotado <= 0) return frete;
        var fatia = p.Items.Where(i => quotationItemIds.Contains(i.QuotationItemId)).Sum(i => i.UnitPrice * i.Quantity);
        return Math.Round(frete * (fatia / cotado), 2);
    }

    /// <summary>Consumo do contrato: soma das O.C.s não canceladas do fornecedor dentro da vigência.</summary>
    public async Task<decimal> ContractConsumedAsync(Supplier supplier, CancellationToken ct = default)
    {
        var inicio = supplier.ContractValidFrom?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;
        var fim = supplier.ContractValidUntil?.ToDateTime(TimeOnly.MaxValue) ?? DateTime.MaxValue;
        var i0 = new DateTimeOffset(inicio, TimeSpan.Zero);
        var f0 = new DateTimeOffset(fim, TimeSpan.Zero);
        return await db.PurchaseOrders
            .Where(o => o.SupplierId == supplier.Id && o.Status != PurchaseOrderStatus.Cancelled
                        && o.CreatedAt >= i0 && o.CreatedAt <= f0)
            .SumAsync(o => (decimal?)o.TotalValue, ct) ?? 0m;
    }

    public Task<(Quotation? q, UserError? error)> CancelAsync(Actor actor, Guid id, string? reason, CancellationToken ct = default) =>
        TransitionAsync(actor, id, null, QuotationStatus.Cancelled, "COTACAO_CANCELADA",
            "Processo de cotação cancelado.", reason, ct,
            guard: q => q.Status is QuotationStatus.PoIssued or QuotationStatus.Rejected or QuotationStatus.Cancelled
                ? new("RFQ-ERR-020", "Processo já concluído não pode ser cancelado.")
                : string.IsNullOrWhiteSpace(reason) ? new("RFQ-ERR-021", "Informe o motivo do cancelamento.") : null);

    public void RecordPdfEvent(Quotation q, Actor actor, Guid documentId, string poNumber)
        => AddEvent(q, "PDF_GERADO", $"PDF da OC {poNumber} gerado.", actor, null, null, null, documentId);

    /// <summary>Arquiva no histórico a cotação recebida fora do portal (PDF/planilha do fornecedor).</summary>
    public void RecordAttachmentEvent(Quotation q, Actor actor, string supplierName, string fileName)
        => AddEvent(q, "COTACAO_ANEXADA", $"Cotação de {supplierName} anexada ao processo ({fileName}).", actor);

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
        var seq = kind == QuotationKind.Bid ? "bid_number_seq" : "rfq_number_seq";
        if (!db.Database.IsRelational())
            return await db.Quotations.LongCountAsync(q => (q.Kind == QuotationKind.Bid) == (kind == QuotationKind.Bid), ct) + 1;
        return await db.Database
            .SqlQueryRaw<long>($"SELECT nextval('procurement.{seq}') AS \"Value\"")
            .SingleAsync(ct);
    }

}
