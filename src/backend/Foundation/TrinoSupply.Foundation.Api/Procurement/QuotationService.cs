using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

public record ProposalInput(
    int? DeliveryDays, string? PaymentTerms, decimal? FreightValue, DateOnly? ValidUntil,
    string? Notes, IReadOnlyList<ProposalItemInput> Items);
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
        var activeQuotationPrs = await db.Quotations
            .Where(q => q.Status != QuotationStatus.Cancelled && q.Status != QuotationStatus.Rejected)
            .Select(q => q.SourcePrId).ToListAsync(ct);
        var linkedPoPrs = await db.PurchaseOrders
            .Where(o => o.SourcePrId != null && o.Status != PurchaseOrderStatus.Cancelled)
            .Select(o => o.SourcePrId!.Value).ToListAsync(ct);
        return await db.Requisitions.Include(r => r.Items)
            .Where(r => r.Status == RequisitionStatus.Approved
                        && !activeQuotationPrs.Contains(r.Id) && !linkedPoPrs.Contains(r.Id))
            .OrderBy(r => r.DecidedAt).Take(100).ToListAsync(ct);
    }

    // ---- abertura (RFQ-BR-001/002) ------------------------------------------
    public async Task<(Quotation? q, UserError? error)> CreateFromPrAsync(
        Actor actor, Guid prId, QuotationKind kind, DateOnly? deadline, string? notes, CancellationToken ct = default)
    {
        var pr = await db.Requisitions.Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == prId, ct);
        if (pr is null || pr.Status != RequisitionStatus.Approved)
            return (null, new("RFQ-ERR-001", "A requisição de origem não está aprovada."));
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
        if (q.Status != QuotationStatus.Open)
            return (null, new("RFQ-ERR-020", "Somente cotações abertas recebem convites."));

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
            db.QuotationSuppliers.Add(invite); // Add explícito: chave pré-gerada em pai já rastreado
            q.Suppliers.Add(invite);
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
        if (q.Status != QuotationStatus.Open)
            return (null, new("RFQ-ERR-020", "A cotação não está mais aberta para propostas."));
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
            FreightValue = input.FreightValue,
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
        proposal.TotalValue = proposal.Items.Sum(i => i.UnitPrice * i.Quantity) + (proposal.FreightValue ?? 0);
        db.Proposals.Add(proposal);
        AddEvent(q, "PROPOSTA_RECEBIDA",
            $"Proposta v{version} de {proposal.SupplierName} recebida ({via}) — total {proposal.TotalValue:0.00}.",
            new Actor(Guid.Empty, submittedByLabel, "Supplier"));
        await TouchAndSaveAsync(q, ct);
        return (proposal, null);
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
        if (actor.Role == Roles.Approver)
        {
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
        if (actor.Role != Roles.SystemAdministrator &&
            await LinkedDirectorAsync(q, ct) is { } linkedDirector && linkedDirector != actor.Id)
            return (null, new("RFQ-ERR-032", "Alçada por diretoria: este processo está vinculado a outro diretor responsável."));
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
                    AddEvent(q, "DIRETOR_APROVOU",
                        "Diretoria aprovou. Processo APROVADO PARA EMISSÃO DA OC — tarefa disponível para Suprimentos.",
                        actor, from, q.Status, reason);
                }
                else
                {
                    q.ManagerApprovedBy = actor.Id;
                    q.ManagerApprovedByLabel = actor.Label;
                    q.ManagerApprovedAt = clock.GetUtcNow();
                    q.Status = QuotationStatus.AwaitingDirector;
                    AddEvent(q, "GERENTE_APROVOU",
                        "Aprovação gerencial concedida. Processo encaminhado à diretoria.",
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

    // ---- emissão da OC (RFQ-BR-007; PO-BR-005 na entrega) --------------------
    public async Task<(PurchaseOrder? order, UserError? error)> IssuePurchaseOrderAsync(
        Actor actor, Guid id, string? notes, CancellationToken ct = default)
    {
        var q = await GetAsync(id, ct);
        if (q is null) return (null, new("RFQ-ERR-404", "Cotação não encontrada."));
        if (q.Status != QuotationStatus.ApprovedForIssue)
            return (null, new("RFQ-ERR-040", "A OC só pode ser emitida com o processo APROVADO PARA EMISSÃO."));
        var proposal = q.Proposals.Single(p => p.Id == q.WinnerProposalId);
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == q.WinnerSupplierId, ct);
        if (supplier is null || !supplier.Active)
            return (null, new("RFQ-ERR-040", "Fornecedor vencedor inativo: regularize o cadastro ou solicite ajustes."));

        var now = clock.GetUtcNow();
        var order = new PurchaseOrder
        {
            Number = $"PO-{now.Year}-{await NextPoSeqAsync(ct):000000}",
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
        order.TotalValue = order.Items.Sum(i => (i.UnitPrice ?? 0) * i.Quantity) + (proposal.FreightValue ?? 0);
        if (order.Items.Count == 0 || order.TotalValue <= 0)
            return (null, new("RFQ-ERR-040", "A OC precisa de itens e valor."));
        db.PurchaseOrders.Add(order);

        var from = q.Status;
        q.Status = QuotationStatus.PoIssued;
        q.PurchaseOrderId = order.Id;
        q.PurchaseOrderNumber = order.Number;
        AddEvent(q, "OC_EMITIDA",
            $"Ordem de compra {order.Number} emitida para {order.SupplierName} — total {order.TotalValue:0.00}.",
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

    private async Task<long> NextPoSeqAsync(CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return await db.PurchaseOrders.LongCountAsync(ct) + 1;
        return await db.Database
            .SqlQueryRaw<long>("SELECT nextval('procurement.po_number_seq') AS \"Value\"")
            .SingleAsync(ct);
    }
}
