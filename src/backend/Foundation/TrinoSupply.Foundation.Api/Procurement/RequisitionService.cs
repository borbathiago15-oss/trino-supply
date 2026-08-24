using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

public interface IPrNumberGenerator
{
    Task<string> NextAsync(CancellationToken ct = default);
}

public record Actor(Guid Id, string Label, string Role)
{
    public bool IsAdmin => Role == Roles.SystemAdministrator;
    public bool CanCreate => Role is Roles.Requester or Roles.SupplyManager || IsAdmin;
    public bool CanDecide => Role is Roles.Approver or Roles.SupplyManager || IsAdmin;
    public bool SeesAll => Role is Roles.Approver or Roles.SupplyManager or Roles.Auditor or Roles.PurchasingOfficer || IsAdmin;
    public bool CanAccessModule => CanCreate || CanDecide || Role is Roles.Auditor or Roles.PurchasingOfficer;
}

/// <summary>Quem aprova a SC (gerente do CC) e o impedimento, quando houver.</summary>
public record ApproverHint(Guid? ApproverId, string? ApproverLabel, string? Issue);

public record ItemInput(string Description, decimal Quantity, string? UnitOfMeasure, decimal? EstimatedUnitPrice, string? Notes, Guid? CatalogItemId = null);

/// <summary>
/// Serviço de domínio do PR-001 (MVP): transições exclusivamente pela máquina de estados
/// PR-001-03; erros do catálogo PR-ERR (PR-001-13 §5). Aprovação em nível único até o
/// Workflow Engine (FD-001-04) existir; validação da submissão é síncrona (ST-002/003 transientes).
/// </summary>
public class RequisitionService(AppDbContext db, IPrNumberGenerator numbers, Catalog.CatalogService catalog, TimeProvider clock)
{
    // ---- consulta -----------------------------------------------------------
    public async Task<List<PurchaseRequisition>> ListAsync(Actor actor, RequisitionStatus? status, CancellationToken ct = default)
    {
        var query = db.Requisitions.Include(r => r.Items)
            .Where(r => r.DeletedAt == null);
        if (!actor.SeesAll) query = query.Where(r => r.RequesterId == actor.Id);
        if (status is not null) query = query.Where(r => r.Status == status);
        return await query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Take(100).ToListAsync(ct);
    }

    public async Task<PurchaseRequisition?> GetAsync(Actor actor, Guid id, CancellationToken ct = default)
    {
        var pr = await db.Requisitions.Include(r => r.Items)
            .SingleOrDefaultAsync(r => r.Id == id && r.DeletedAt == null, ct);
        if (pr is null) return null;
        return actor.SeesAll || pr.RequesterId == actor.Id ? pr : null; // fora do escopo ⇒ 404 (anti-enumeração)
    }

    /// <summary>
    /// Centros de custo em que o usuário é o gerente responsável (vínculo do cadastro do CC).
    /// Alçada por CC: o Gerente (Pleno) só aprova o que pertence aos CCs vinculados a ele.
    /// </summary>
    public async Task<List<string>> ManagedCostCentersAsync(Guid userId, CancellationToken ct = default) =>
        await db.CostCenters.Where(c => c.ManagerUserId == userId && c.Active)
            .Select(c => c.Code).ToListAsync(ct);

    /// <summary>Centros de custo que já têm um gerente responsável ativo (código em caixa alta).</summary>
    private async Task<List<string>> CostCentersWithManagerAsync(CancellationToken ct = default) =>
        await db.CostCenters.Where(c => c.Active && c.ManagerUserId != null)
            .Select(c => c.Code.ToUpper()).ToListAsync(ct);

    /// <summary>
    /// Fila de aprovação: IN_APPROVAL, exceto as próprias (SoD); gerente com CCs vinculados vê os seus
    /// e também os centros de custo sem gerente responsável — nenhuma SC pode ficar órfã de aprovador.
    /// </summary>
    public async Task<List<PurchaseRequisition>> PendingApprovalsAsync(Actor actor, CancellationToken ct = default)
    {
        var query = db.Requisitions.Include(r => r.Items)
            .Where(r => r.DeletedAt == null && r.Status == RequisitionStatus.InApproval && r.RequesterId != actor.Id);
        if (actor.Role == Roles.Approver)
        {
            var managed = await ManagedCostCentersAsync(actor.Id, ct);
            if (managed.Count > 0)
            {
                var withManager = await CostCentersWithManagerAsync(ct);
                query = query.Where(r => managed.Contains(r.CostCenter.ToUpper())
                                         || !withManager.Contains(r.CostCenter.ToUpper()));
            }
        }
        return await query.OrderBy(r => r.SubmittedAt).Take(100).ToListAsync(ct);
    }

    /// <summary>
    /// Quem deve aprovar cada SC e o que está travando: usado para mostrar "aguardando aprovação de X"
    /// e para avisar quando o centro de custo não tem gerente vinculado (SC sem aprovador definido).
    /// </summary>
    public async Task<Dictionary<string, ApproverHint>> ApproverHintsAsync(
        IEnumerable<PurchaseRequisition> requisitions, CancellationToken ct = default)
    {
        var codes = requisitions.Select(r => r.CostCenter.Trim().ToUpperInvariant()).Distinct().ToList();
        if (codes.Count == 0) return [];
        var managers = await db.CostCenters
            .Where(c => c.Active && codes.Contains(c.Code.ToUpper()))
            .Select(c => new { c.Code, c.ManagerUserId, c.ManagerName })
            .ToListAsync(ct);
        return codes.ToDictionary(code => code, code =>
        {
            var cc = managers.FirstOrDefault(m => m.Code.ToUpperInvariant() == code);
            if (cc is null)
                return new ApproverHint(null, null, "Centro de custo não cadastrado: a aprovação fica com o Gestor de Suprimentos.");
            if (cc.ManagerUserId is null)
                return new ApproverHint(null, null, "Nenhum gerente vinculado a este centro de custo — vincule em Cadastros → Centros de Custo.");
            return new ApproverHint(cc.ManagerUserId, cc.ManagerName, null);
        });
    }

    public ApproverHint HintFor(Dictionary<string, ApproverHint> hints, PurchaseRequisition pr) =>
        hints.TryGetValue(pr.CostCenter.Trim().ToUpperInvariant(), out var hint) ? hint : new(null, null, null);

    /// <summary>
    /// Gerente (Approver) com CCs vinculados só decide SCs desses CCs — exceto centros de custo
    /// sem gerente responsável, que qualquer aprovador pode decidir (senão a SC fica órfã).
    /// </summary>
    private async Task<UserError?> CheckApprovalScopeAsync(Actor actor, PurchaseRequisition pr, CancellationToken ct)
    {
        if (actor.Role != Roles.Approver) return null;
        var managed = await ManagedCostCentersAsync(actor.Id, ct);
        if (managed.Count == 0 || managed.Contains(pr.CostCenter.ToUpperInvariant())) return null;

        var hasManager = await db.CostCenters.AnyAsync(
            c => c.Active && c.ManagerUserId != null && c.Code.ToUpper() == pr.CostCenter.ToUpper(), ct);
        return hasManager
            ? new("PR-ERR-002", "Este centro de custo não está vinculado à sua alçada de aprovação.")
            : null;
    }

    // ---- ciclo de vida ------------------------------------------------------
    public record ScHeaderInput(string? NeedType, string? DeliveryLocation, string? Company, string? InternalNotes);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public async Task<(PurchaseRequisition? pr, UserError? error)> CreateAsync(
        Actor actor, string justification, string costCenter, string? priority, DateOnly? neededBy,
        IReadOnlyList<ItemInput> items, string? kind = null, ScHeaderInput? header = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(justification))
            return (null, new("PR-ERR-030", "Informe a justificativa da solicitação."));
        if (string.IsNullOrWhiteSpace(costCenter))
            return (null, new("PR-ERR-021", "Informe o centro de custo."));
        priority ??= "NORMAL";
        if (!RequisitionPriorities.All.Contains(priority))
            return (null, new("PR-ERR-030", "Prioridade inválida."));
        kind = (kind ?? "AVULSA").ToUpperInvariant();
        if (kind is not ("AVULSA" or "CATALOGO"))
            return (null, new("PR-ERR-030", "Tipo de requisição inválido (AVULSA ou CATALOGO)."));

        // Alçada por CC: Júnior/Pleno com centros vinculados só solicitam dos seus centros
        if (await CheckCcLinkAsync(actor, costCenter, ct) is { } ccLinkError)
            return (null, ccLinkError);
        if (kind == "CATALOGO" && items.Any(i => i.CatalogItemId is null))
            return (null, new("PR-ERR-030", "Em requisição por catálogo, todos os itens devem vir do catálogo."));

        var (catalogItems, catalogError) = await catalog.ResolveForRequisitionAsync(
            items.Where(i => i.CatalogItemId is not null).Select(i => i.CatalogItemId!.Value).ToList(), ct);
        if (catalogError is not null) return (null, catalogError);

        var now = clock.GetUtcNow();
        var pr = new PurchaseRequisition
        {
            Number = await numbers.NextAsync(ct),
            Kind = kind,
            Justification = justification.Trim(),
            CostCenter = costCenter.Trim(),
            Priority = priority,
            NeededBy = neededBy,
            NeedType = Clean(header?.NeedType)?.ToUpperInvariant(),
            DeliveryLocation = Clean(header?.DeliveryLocation),
            Company = Clean(header?.Company),
            InternalNotes = Clean(header?.InternalNotes),
            RequesterId = actor.Id,
            RequesterLabel = actor.Label,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var seq = 0;
        foreach (var input in items)
        {
            var (item, error) = BuildItem(input, ++seq, now, catalogItems!);
            if (error is not null) return (null, error);
            pr.Items.Add(item!);
        }
        db.Requisitions.Add(pr);
        await db.SaveChangesAsync(ct);
        return (pr, null); // EVT-001 RequisitionCreated (outbox: incremento futuro)
    }

    /// <summary>Júnior/Pleno com centros vinculados só solicitam dos seus centros (PR-ERR-021).</summary>
    private async Task<UserError?> CheckCcLinkAsync(Actor actor, string costCenter, CancellationToken ct)
    {
        if (actor.Role is not (Roles.Requester or Roles.Approver)) return null;
        var linked = await db.Users.Where(u => u.Id == actor.Id).Select(u => u.CostCenters).SingleOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(linked)) return null;
        var codes = linked.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return codes.Contains(costCenter.Trim().ToUpperInvariant()) ? null
            : new("PR-ERR-021", "Centro de custo fora do seu vínculo: solicite apenas dos centros vinculados ao seu usuário.");
    }

    public async Task<(PurchaseRequisition? pr, UserError? error)> UpdateHeaderAsync(
        Actor actor, Guid id, string? justification, string? costCenter, string? priority, DateOnly? neededBy,
        bool clearNeededBy, CancellationToken ct = default)
    {
        var (pr, error) = await GetEditableAsync(actor, id, ct);
        if (error is not null) return (null, error);

        if (justification is not null)
        {
            if (string.IsNullOrWhiteSpace(justification)) return (null, new("PR-ERR-030", "A justificativa não pode ficar vazia."));
            pr!.Justification = justification.Trim();
        }
        if (costCenter is not null)
        {
            if (string.IsNullOrWhiteSpace(costCenter)) return (null, new("PR-ERR-021", "O centro de custo não pode ficar vazio."));
            if (await CheckCcLinkAsync(actor, costCenter, ct) is { } ccLinkError) return (null, ccLinkError);
            pr!.CostCenter = costCenter.Trim();
        }
        if (priority is not null)
        {
            if (!RequisitionPriorities.All.Contains(priority)) return (null, new("PR-ERR-030", "Prioridade inválida."));
            pr!.Priority = priority;
        }
        if (clearNeededBy) pr!.NeededBy = null;
        else if (neededBy is not null) pr!.NeededBy = neededBy;

        await TouchAndSaveAsync(pr!, ct);
        return (pr, null);
    }

    public async Task<UserError?> DeleteDraftAsync(Actor actor, Guid id, CancellationToken ct = default)
    {
        var pr = await GetAsync(actor, id, ct);
        if (pr is null) return new("PR-ERR-404", "Requisição não encontrada.");
        if (pr.Status != RequisitionStatus.Draft)
            return new("PR-ERR-040", "Somente rascunhos podem ser excluídos.");
        if (pr.RequesterId != actor.Id && !actor.IsAdmin)
            return new("PR-ERR-001", "Somente o titular pode excluir o rascunho.");
        pr.DeletedAt = clock.GetUtcNow();
        pr.DeletedBy = actor.Id;
        await TouchAndSaveAsync(pr, ct);
        return null;
    }

    /// <summary>Submete: Draft/Returned → (validação síncrona) → InApproval; resubmissão incrementa o ciclo.</summary>
    public async Task<(PurchaseRequisition? pr, UserError? error)> SubmitAsync(Actor actor, Guid id, CancellationToken ct = default)
    {
        var pr = await GetAsync(actor, id, ct);
        if (pr is null) return (null, new("PR-ERR-404", "Requisição não encontrada."));
        if (pr.RequesterId != actor.Id && !actor.IsAdmin)
            return (null, new("PR-ERR-001", "Somente o titular pode submeter a requisição."));
        if (!pr.IsEditable)
            return (null, new("PR-ERR-040", "O estado atual não permite submissão."));

        // Guards da submissão (PR-BR-020/021; PR-ERR-030/050)
        if (pr.Items.Count == 0)
            return (null, new("PR-ERR-030", "Inclua ao menos um item antes de submeter."));
        if (string.IsNullOrWhiteSpace(pr.Justification) || string.IsNullOrWhiteSpace(pr.CostCenter))
            return (null, new("PR-ERR-030", "Justificativa e centro de custo são obrigatórios para submeter."));
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (pr.NeededBy is not null && pr.NeededBy < today)
            return (null, new("PR-ERR-050", "A data de necessidade não pode estar no passado."));

        var isResubmission = pr.Status == RequisitionStatus.Returned;
        pr.Status = RequisitionStatus.InApproval; // ST-002/ST-003 transientes no MVP (validação síncrona OK)
        pr.SubmittedAt = clock.GetUtcNow();
        pr.DecisionReason = null;
        pr.DecidedAt = null;
        pr.DecidedById = null;
        pr.DecidedByLabel = null;
        if (isResubmission) pr.Cycle += 1;
        await TouchAndSaveAsync(pr, ct);
        return (pr, null); // EVT-002 Submitted + ApprovalStarted
    }

    public Task<(PurchaseRequisition? pr, UserError? error)> ApproveAsync(Actor actor, Guid id, string? comments, CancellationToken ct = default) =>
        DecideAsync(actor, id, RequisitionStatus.Approved, comments, reasonRequired: false, ct);

    public Task<(PurchaseRequisition? pr, UserError? error)> RejectAsync(Actor actor, Guid id, string? reason, CancellationToken ct = default) =>
        DecideAsync(actor, id, RequisitionStatus.Rejected, reason, reasonRequired: true, ct);

    public Task<(PurchaseRequisition? pr, UserError? error)> ReturnAsync(Actor actor, Guid id, string? reason, CancellationToken ct = default) =>
        DecideAsync(actor, id, RequisitionStatus.Returned, reason, reasonRequired: true, ct);

    public async Task<(PurchaseRequisition? pr, UserError? error)> CancelAsync(Actor actor, Guid id, string? reason, CancellationToken ct = default)
    {
        var pr = await GetAsync(actor, id, ct);
        if (pr is null) return (null, new("PR-ERR-404", "Requisição não encontrada."));
        if (pr.RequesterId != actor.Id && !actor.IsAdmin)
            return (null, new("PR-ERR-001", "Somente o titular pode cancelar a requisição."));
        if (string.IsNullOrWhiteSpace(reason))
            return (null, new("PR-ERR-030", "Informe o motivo do cancelamento."));
        // Política padrão: cancel-after-approval = false (PR-001-03, ST-005)
        if (pr.Status is not (RequisitionStatus.Draft or RequisitionStatus.Returned or RequisitionStatus.InApproval))
            return (null, new("PR-ERR-040", "O estado atual não permite cancelamento."));

        pr.Status = RequisitionStatus.Cancelled;
        pr.DecisionReason = reason.Trim();
        pr.DecidedAt = clock.GetUtcNow();
        pr.DecidedById = actor.Id;
        pr.DecidedByLabel = actor.Label;
        await TouchAndSaveAsync(pr, ct);
        return (pr, null); // EVT-009 Cancelled
    }

    // ---- itens --------------------------------------------------------------
    public async Task<(PurchaseRequisition? pr, UserError? error)> AddItemAsync(Actor actor, Guid id, ItemInput input, CancellationToken ct = default)
    {
        var (pr, error) = await GetEditableAsync(actor, id, ct);
        if (error is not null) return (null, error);
        if (pr!.Kind == "CATALOGO" && input.CatalogItemId is null)
            return (null, new("PR-ERR-030", "Esta requisição é por catálogo: adicione itens do catálogo."));
        var (catalogItems, catalogError) = await catalog.ResolveForRequisitionAsync(
            input.CatalogItemId is null ? [] : [input.CatalogItemId.Value], ct);
        if (catalogError is not null) return (null, catalogError);
        var (item, itemError) = BuildItem(input, pr.Items.Count == 0 ? 1 : pr.Items.Max(i => i.Sequence) + 1, clock.GetUtcNow(), catalogItems!);
        if (itemError is not null) return (null, itemError);
        item!.RequisitionId = pr.Id;
        db.RequisitionItems.Add(item); // Add explícito: chave pré-gerada em pai já rastreado ficaria Modified
        pr.Items.Add(item);
        await TouchAndSaveAsync(pr, ct);
        return (pr, null); // EVT-003 ItemAdded
    }

    public async Task<(PurchaseRequisition? pr, UserError? error)> RemoveItemAsync(Actor actor, Guid id, Guid itemId, CancellationToken ct = default)
    {
        var (pr, error) = await GetEditableAsync(actor, id, ct);
        if (error is not null) return (null, error);
        var item = pr!.Items.SingleOrDefault(i => i.Id == itemId);
        if (item is null) return (null, new("PR-ERR-404", "Item não encontrado."));
        pr.Items.Remove(item);
        db.RequisitionItems.Remove(item);
        await TouchAndSaveAsync(pr, ct);
        return (pr, null); // EVT-004 ItemRemoved
    }

    // ---- internos -----------------------------------------------------------
    private async Task<(PurchaseRequisition? pr, UserError? error)> DecideAsync(
        Actor actor, Guid id, RequisitionStatus target, string? reasonOrComments, bool reasonRequired, CancellationToken ct)
    {
        var pr = await GetAsync(actor, id, ct);
        if (pr is null) return (null, new("PR-ERR-404", "Requisição não encontrada."));
        if (pr.Status != RequisitionStatus.InApproval)
            return (null, new("PR-ERR-040", "A requisição não está aguardando aprovação."));
        if (pr.RequesterId == actor.Id)
            return (null, new("PR-ERR-041", "O solicitante não pode decidir a própria requisição (segregação de funções)."));
        if (await CheckApprovalScopeAsync(actor, pr, ct) is { } scopeError)
            return (null, scopeError);
        if (reasonRequired && string.IsNullOrWhiteSpace(reasonOrComments))
            return (null, new("PR-ERR-030", "Informe o motivo da decisão."));

        pr.Status = target;
        pr.DecisionReason = string.IsNullOrWhiteSpace(reasonOrComments) ? null : reasonOrComments.Trim();
        pr.DecidedAt = clock.GetUtcNow();
        pr.DecidedById = actor.Id;
        pr.DecidedByLabel = actor.Label;
        await TouchAndSaveAsync(pr, ct);
        return (pr, null); // EVT-006/007/008 conforme o desfecho
    }

    private async Task<(PurchaseRequisition? pr, UserError? error)> GetEditableAsync(Actor actor, Guid id, CancellationToken ct)
    {
        var pr = await GetAsync(actor, id, ct);
        if (pr is null) return (null, new("PR-ERR-404", "Requisição não encontrada."));
        if (pr.RequesterId != actor.Id && !actor.IsAdmin)
            return (null, new("PR-ERR-001", "Somente o titular pode alterar a requisição."));
        if (!pr.IsEditable)
            return (null, new("PR-ERR-040", "A requisição não está em um estado editável (somente Rascunho ou Devolvida)."));
        return (pr, null);
    }

    private static (RequisitionItem? item, UserError? error) BuildItem(
        ItemInput input, int sequence, DateTimeOffset now, IReadOnlyDictionary<Guid, Catalog.CatalogItem> catalogItems)
    {
        if (input.Quantity <= 0)
            return (null, new("PR-ERR-010", "A quantidade deve ser maior que zero."));

        // Item do catálogo: snapshot de descrição/unidade/preço na data da solicitação (MMS-002)
        if (input.CatalogItemId is { } catalogId)
        {
            var c = catalogItems[catalogId];
            return (new RequisitionItem
            {
                CatalogItemId = c.Id,
                CatalogCode = c.Code,
                Sequence = sequence,
                Description = c.Description,
                Quantity = input.Quantity,
                UnitOfMeasure = c.UnitOfMeasure,
                EstimatedUnitPrice = input.EstimatedUnitPrice ?? c.ReferencePrice,
                Notes = input.Notes,
                CreatedAt = now,
            }, null);
        }

        if (string.IsNullOrWhiteSpace(input.Description) || input.Description.Trim().Length < 3)
            return (null, new("PR-ERR-030", "Descreva o item (mínimo 3 caracteres)."));
        if (input.EstimatedUnitPrice is < 0)
            return (null, new("PR-ERR-030", "O preço estimado não pode ser negativo."));
        return (new RequisitionItem
        {
            Sequence = sequence,
            Description = input.Description.Trim(),
            Quantity = input.Quantity,
            UnitOfMeasure = string.IsNullOrWhiteSpace(input.UnitOfMeasure) ? "UN" : input.UnitOfMeasure.Trim().ToUpperInvariant(),
            EstimatedUnitPrice = input.EstimatedUnitPrice,
            Notes = input.Notes,
            CreatedAt = now,
        }, null);
    }

    private async Task TouchAndSaveAsync(PurchaseRequisition pr, CancellationToken ct)
    {
        pr.UpdatedAt = clock.GetUtcNow();
        pr.Version += 1;
        await db.SaveChangesAsync(ct);
    }
}
