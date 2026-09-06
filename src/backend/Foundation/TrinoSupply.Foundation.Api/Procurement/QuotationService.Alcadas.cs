using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// As duas alçadas (RFQ-BR-006/007) e a segregação de funções que as separa: quem
/// escolheu o fornecedor não aprova a própria escolha, e quem deu o Nível 1 não dá o
/// Nível 2 (RFQ-ERR-030).
/// </summary>
public partial class QuotationService
{
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
}
