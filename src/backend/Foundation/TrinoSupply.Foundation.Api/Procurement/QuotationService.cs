using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

public record ProposalInput(
    int? DeliveryDays, string? PaymentTerms, decimal? FreightValue, DateOnly? ValidUntil,
    string? Notes, IReadOnlyList<ProposalItemInput> Items,
    decimal? DiscountValue = null, string? Currency = null, int? PaymentDays = null);
/// <summary>SC já designada a um comprador, mas ainda retida na aprovação.</summary>
public record QueueBlocked(PurchaseRequisition Pr, string Reason);

public record ProposalItemInput(Guid QuotationItemId, decimal UnitPrice, decimal? Quantity);

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
        db.Quotations.Include(q => q.Items).Include(q => q.Suppliers)
            .Include(q => q.Proposals).ThenInclude(p => p.Items)
            .OrderByDescending(q => q.CreatedAt).Take(200).ToListAsync(ct);

    public Task<Quotation?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Quotations.Include(q => q.Items).Include(q => q.Suppliers)
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
        var activeQuotationPrs = await db.Quotations
            .Where(q => q.Status != QuotationStatus.Cancelled && q.Status != QuotationStatus.Rejected)
            .Select(q => q.SourcePrId).ToListAsync(ct);
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
        if (await db.Quotations.AnyAsync(q => q.SourcePrId == prId
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
        var seqNo = 0;
        foreach (var i in pr.Items.OrderBy(i => i.Sequence))
            q.Items.Add(new QuotationItem
            {
                QuotationId = q.Id, Sequence = ++seqNo, CatalogItemId = i.CatalogItemId,
                CatalogCode = i.CatalogCode, Description = i.Description,
                Quantity = i.Quantity, UnitOfMeasure = i.UnitOfMeasure,
            });
        db.Quotations.Add(q);
        AddEvent(q, "COTACAO_ABERTA", $"Cotação {q.Number} aberta a partir da requisição {pr.Number}.",
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
            if (s is null || !s.Active)
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
        proposal.TotalValue = proposal.Items.Sum(i => i.UnitPrice * i.Quantity)
            + (proposal.FreightValue ?? 0) - (proposal.DiscountValue ?? 0);
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
        var bruto = atual.Items.Sum(i => i.UnitPrice * i.Quantity) + (atual.FreightValue ?? 0);
        var input = new ProposalInput(
            atual.DeliveryDays, atual.PaymentTerms, atual.FreightValue, atual.ValidUntil,
            string.IsNullOrWhiteSpace(notes) ? atual.Notes : notes.Trim(),
            atual.Items.Select(i => new ProposalItemInput(i.QuotationItemId, i.UnitPrice, i.Quantity)).ToList(),
            bruto - fechado.Value, atual.Currency, atual.PaymentDays);
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
    public async Task<(Quotation? q, UserError? error)> SelectWinnerAsync(
        Actor actor, Guid id, Guid proposalId, string? criteria, string? justification, CancellationToken ct = default)
    {
        var q = await GetAsync(id, ct);
        if (q is null) return (null, new("RFQ-ERR-404", "Cotação não encontrada."));
        if (q.Status != QuotationStatus.Analysis)
            return (null, new("RFQ-ERR-020", "A escolha do fornecedor acontece na etapa de análise."));
        if (string.IsNullOrWhiteSpace(justification))
            return (null, new("RFQ-ERR-021", "A justificativa da escolha é obrigatória."));
        var proposal = q.Proposals.SingleOrDefault(p => p.Id == proposalId);
        if (proposal is null) return (null, new("RFQ-ERR-021", "Proposta vencedora inexistente nesta cotação."));
        var latest = q.Proposals.Where(p => p.SupplierId == proposal.SupplierId)
            .OrderByDescending(p => p.VersionNumber).First();
        if (latest.Id != proposal.Id)
            return (null, new("RFQ-ERR-021", "Selecione a versão mais recente da proposta do fornecedor."));

        q.WinnerSupplierId = proposal.SupplierId;
        q.WinnerProposalId = proposal.Id;
        ApplySaving(q, proposal);
        q.SelectionCriteria = string.IsNullOrWhiteSpace(criteria) ? null : criteria.Trim();
        q.SelectionJustification = justification.Trim();
        q.SelectedBy = actor.Id;
        q.SelectedByLabel = actor.Label;
        q.SelectedAt = clock.GetUtcNow();
        var from = q.Status;
        q.Status = QuotationStatus.AwaitingManager;
        AddEvent(q, "FORNECEDOR_SELECIONADO",
            $"Fornecedor {proposal.SupplierName} selecionado (proposta v{proposal.VersionNumber}, total {proposal.TotalValue:0.00}). Processo encaminhado à aprovação gerencial.",
            actor, from, q.Status, justification.Trim());
        await TouchAndSaveAsync(q, ct);
        return (q, null);
    }

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

    /// <summary>Compra autorizada pela diretoria: a SC de origem passa a APROVADA (autorização com preço).</summary>
    private async Task MarkSourcePrApprovedAsync(Quotation q, Actor actor, CancellationToken ct)
    {
        var pr = await db.Requisitions.SingleOrDefaultAsync(r => r.Id == q.SourcePrId, ct);
        if (pr is null || pr.Status == RequisitionStatus.Approved) return;
        pr.Status = RequisitionStatus.Approved;
        pr.DecidedById = actor.Id;
        pr.DecidedByLabel = actor.Label;
        pr.DecidedAt = clock.GetUtcNow();
        pr.DecisionReason = $"Compra aprovada no processo {q.Number}.";
        pr.UpdatedAt = clock.GetUtcNow();
        pr.Version += 1;
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
    /// </summary>
    public async Task<(PurchaseOrder? order, UserError? error)> RegisterErpPurchaseOrderAsync(
        Actor actor, Guid id, string? erpNumber, DateOnly? issuedOn, string? notes, CancellationToken ct = default)
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
        var proposal = q.Proposals.Single(p => p.Id == q.WinnerProposalId);
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == q.WinnerSupplierId, ct);
        if (supplier is null || !supplier.Active)
            return (null, new("RFQ-ERR-040", "Fornecedor vencedor inativo: regularize o cadastro ou solicite ajustes."));

        var now = clock.GetUtcNow();
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
            PaymentTerms = proposal.PaymentTerms,
            DeliveryDays = proposal.DeliveryDays,
            FreightValue = proposal.FreightValue,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            IssuedBy = actor.Id,
            IssuedByLabel = actor.Label,
            CreatedAt = now,
            UpdatedAt = now,
        };
        foreach (var pi in proposal.Items)
        {
            var qi = q.Items.Single(x => x.Id == pi.QuotationItemId);
            order.Items.Add(new PurchaseOrderItem
            {
                Description = qi.Description, UnitOfMeasure = qi.UnitOfMeasure,
                Quantity = pi.Quantity, UnitPrice = pi.UnitPrice,
                CatalogItemId = qi.CatalogItemId, CatalogCode = qi.CatalogCode,
                CreatedAt = now,
            });
        }
        order.TotalValue = order.Items.Sum(i => (i.UnitPrice ?? 0) * i.Quantity) + (proposal.FreightValue ?? 0)
            - (proposal.DiscountValue ?? 0);
        if (order.Items.Count == 0 || order.TotalValue <= 0)
            return (null, new("RFQ-ERR-040", "A OC precisa de itens e valor."));
        db.PurchaseOrders.Add(order);

        var from = q.Status;
        q.Status = QuotationStatus.PoIssued;
        q.PurchaseOrderId = order.Id;
        q.PurchaseOrderNumber = order.Number;
        AddEvent(q, "OC_REGISTRADA",
            $"OC {order.Number} do SENIOR registrada para {order.SupplierName} — total {order.TotalValue:0.00}.",
            actor, from, q.Status);
        await TouchAndSaveAsync(q, ct);
        return (order, null);
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
