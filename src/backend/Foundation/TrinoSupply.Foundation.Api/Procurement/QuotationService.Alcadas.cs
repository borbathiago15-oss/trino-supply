using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// As duas alçadas (RFQ-BR-006/007) e a segregação de funções entre elas (RFQ-ERR-030),
/// que vale <b>no Nível 2</b>: o diretor não pode ser quem escolheu o fornecedor nem quem
/// deu o Nível 1. No Nível 1 não há segregação — decisão da empresa (2026-09): o comprador
/// abre a SC em qualquer centro, cota e fecha a primeira alçada do próprio processo; a
/// separação de funções fica garantida pela segunda.
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
        if (await ImpedimentoNivel1Async(q, actor, ct) is { } impedimento) return (null, impedimento);
        return await DecideAsync(q, actor, decision, reason, isDirector: false, ct);
    }

    /// <summary>
    /// Por que esta pessoa <b>não</b> pode dar o Nível 1 deste processo — nulo quando pode.
    /// É a mesma régua da fila da Central: o que aparece lá é exatamente o que ela decide.
    /// Alçada do centro: qualquer pessoa da lista resolve a etapa. O comprador e o
    /// administrador decidem em qualquer centro — a lista do centro é para os gestores.
    /// </summary>
    internal async Task<UserError?> ImpedimentoNivel1Async(Quotation q, Actor actor, CancellationToken ct)
    {
        if (actor.Role is Roles.SystemAdministrator or Roles.PurchasingOfficer) return null;
        if (await ApprovalLevels.CanDecideAsync(db, q.CostCenter, ApprovalLevels.Level1, actor.Id, ct) is { } noNivel1)
            return noNivel1 ? null
                : new("RFQ-ERR-031", "Alçada por centro de custo: a 1ª aprovação deste processo é do Nível 1 do centro (" +
                    await ApprovalLevels.LabelAsync(db, q.CostCenter, ApprovalLevels.Level1, ct) + ").");
        if (actor.Role == Roles.Approver)
        {
            // centro sem Nível 1 cadastrado: vale o gerente responsável antigo
            var cc = q.CostCenter.Trim().ToUpperInvariant();
            var manages = await db.CostCenters.AnyAsync(
                c => c.Active && c.ManagerUserId == actor.Id && c.Code == cc, ct);
            if (!manages)
                return new("RFQ-ERR-031", "Alçada por centro de custo: este processo pertence a um centro de custo que não está sob a sua gerência.");
        }
        return null;
    }

    /// <summary>
    /// Por que esta pessoa <b>não</b> pode dar o Nível 2 deste processo — nulo quando pode:
    /// segregação (RFQ-ERR-030), a lista do Nível 2 do centro e, sem lista, o diretor vinculado.
    /// </summary>
    internal async Task<UserError?> ImpedimentoNivel2Async(Quotation q, Actor actor, CancellationToken ct)
    {
        if (actor.Id == q.SelectedBy || actor.Id == q.ManagerApprovedBy)
            return new("RFQ-ERR-030", "Segregação de funções: o Diretor não pode ser quem selecionou nem quem deu a aprovação gerencial.");
        if (actor.Role == Roles.SystemAdministrator) return null;
        // alçada do centro (Nível 2): qualquer pessoa da lista resolve a etapa
        if (await ApprovalLevels.CanDecideAsync(db, q.CostCenter, ApprovalLevels.Level2, actor.Id, ct) is { } noNivel2)
            return noNivel2 ? null
                : new("RFQ-ERR-032", "Alçada por centro de custo: a 2ª aprovação deste processo é do Nível 2 do centro (" +
                    await ApprovalLevels.LabelAsync(db, q.CostCenter, ApprovalLevels.Level2, ct) + ").");
        // centro sem Nível 2 cadastrado: vale o diretor vinculado ao gerente
        if (await LinkedDirectorAsync(q, ct) is { } linkedDirector && linkedDirector != actor.Id)
            return new("RFQ-ERR-032", "Alçada por diretoria: este processo está vinculado a outro diretor responsável.");
        return null;
    }

    public async Task<(Quotation? q, UserError? error)> DirectorDecisionAsync(
        Actor actor, Guid id, string decision, string? reason, CancellationToken ct = default)
    {
        var q = await GetAsync(id, ct);
        if (q is null) return (null, new("RFQ-ERR-404", "Cotação não encontrada."));
        if (q.Status != QuotationStatus.AwaitingDirector)
            return (null, new("RFQ-ERR-020", "O processo não está aguardando aprovação da diretoria."));
        if (await ImpedimentoNivel2Async(q, actor, ct) is { } impedimento) return (null, impedimento);
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
                        "Aprovador 02 (Nível 2) aprovou. Pedido(s) criado(s): o comprador registra a O.C. do SENIOR, o faturamento e a entrega na tela do pedido.",
                        actor, from, q.Status, reason);
                    // o pedido nasce aqui, sem O.C.: tudo o que vem depois da aprovação vive numa tela só
                    if (await CriarPedidosAsync(q, actor, ct) is { } erroPedido)
                        return (null, erroPedido);
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
        await AvisarDaEtapaAsync(q, ct);
        await TouchAndSaveAsync(q, ct);
        return (q, null);
    }

    /// <summary>
    /// Avisa quem passou a ter a bola depois da decisão.
    ///
    /// <para>
    /// O aviso sai do <b>estado em que o processo ficou</b>, e não do botão que foi apertado:
    /// é a mesma fonte de que a Torre deriva "de quem estamos esperando", e duas regras para
    /// a mesma pergunta discordariam no primeiro caso de canto.
    /// </para>
    ///
    /// <para>
    /// Aprovador sem usuário cadastrado no nível não recebe nada — e é por isso que a Torre
    /// diz "sem aprovador cadastrado no centro" na linha: o buraco aparece lá, em vez de o
    /// aviso sumir em silêncio.
    /// </para>
    /// </summary>
    internal async Task AvisarDaEtapaAsync(Quotation q, CancellationToken ct)
    {
        var avisos = new AvisoDoUsuarioService(db, clock);
        switch (q.Status)
        {
            // aviso 3 e 4: a bola foi para uma alçada
            case QuotationStatus.AwaitingManager or QuotationStatus.AwaitingDirector:
            {
                var nivel = q.Status == QuotationStatus.AwaitingManager
                    ? ApprovalLevels.Level1 : ApprovalLevels.Level2;
                var tipo = nivel == ApprovalLevels.Level1
                    ? AvisoKinds.AprovacaoNivel1 : AvisoKinds.AprovacaoNivel2;
                var quem = (await ApprovalLevels.OfAsync(db, q.CostCenter, nivel, ct))
                    .Select(a => a.UserId).ToList();
                avisos.EnfileirarParaTodos(quem, tipo,
                    $"{q.Number} aguarda sua aprovação (Nível {nivel})",
                    $"O processo {q.Number} do centro {q.CostCenter} chegou ao Nível {nivel}.",
                    // a chave inclui o nível: o mesmo processo passa pelos dois, e um aviso
                    // só faria o Nível 2 nunca chegar depois de o Nível 1 já ter chegado
                    $"{tipo}:{q.Id}:{nivel}", $"/cotacoes/{q.Id}");
                break;
            }
            // aviso 5: aprovado — volta ao comprador para registrar a O.C. do ERP. O pedido já
            // nasceu na aprovação: o link leva à tela dele, onde ficam O.C., faturamento e entrega
            case QuotationStatus.ApprovedForIssue:
                avisos.Enfileirar(q.CreatedBy, AvisoKinds.LiberadoParaOc,
                    $"{q.Number} aprovado — registre a O.C.",
                    $"As duas alçadas aprovaram o processo {q.Number}. "
                    + "Feche a O.C. no ERP SENIOR e registre o número na tela do pedido.",
                    $"{AvisoKinds.LiberadoParaOc}:{q.Id}",
                    q.PurchaseOrderId is { } pedido ? $"/pedidos/{pedido}" : $"/cotacoes/{q.Id}");
                break;
        }
    }
}
