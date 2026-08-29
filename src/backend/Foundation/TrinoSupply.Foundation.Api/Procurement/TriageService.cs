using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Materials;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>Uma demanda em triagem: SC aprovada (compra) ou solicitação de material (almoxarifado).</summary>
public record TriageTicket(
    string Kind,                 // SC | MATERIAL
    Guid Id, string Number, string CostCenter, string RequesterLabel, string Summary,
    decimal? EstimatedValue, DateTimeOffset? OpenedAt, string Status,
    Guid? AssignedToId, string? AssignedToLabel, string? AssignedByLabel, DateTimeOffset? AssignedAt,
    ProcessStatusView? Process = null,    // situação do fluxo de compras (slide "Status das Solicitações")
    string Priority = "NORMAL", DateOnly? NeededBy = null, string? Justification = null,
    IReadOnlyList<TriageTicketItem>? Items = null,
    string? UrgencyReason = null, string? UrgencyImpact = null,
    string? PriorityChangedByLabel = null, string? PriorityChangeReason = null);

/// <summary>Item da demanda: a tela lista uma linha por item, como no ERP (slides 8 e 9).</summary>
public record TriageTicketItem(
    Guid Id, int Sequence, string? Code, string Description, string? Size,
    decimal Quantity, string UnitOfMeasure, string? Notes);

public record TriageResponsible(Guid Id, string Name, string Role);

/// <summary>
/// Triagem de demandas: o responsável de Suprimentos vê tudo o que chega (SCs aprovadas e
/// solicitações de material) e designa um responsável — comprador ou almoxarife — pela continuidade.
/// A designação vira um "ticket" que aparece na fila pessoal do responsável.
/// </summary>
public class TriageService(AppDbContext db, TimeProvider clock)
{
    /// <summary>Quem faz a triagem (distribui as demandas).</summary>
    public static bool CanTriage(string role) =>
        role is Roles.SupplyManager or Roles.PurchasingOfficer or Roles.SystemAdministrator;

    /// <summary>Papéis que podem receber uma demanda.</summary>
    private static readonly string[] AssignableRoles =
        [Roles.PurchasingOfficer, Roles.SupplyManager, Roles.WarehouseOperator,
         Roles.WarehouseSupervisor, Roles.SystemAdministrator];

    public async Task<List<TriageResponsible>> ResponsiblesAsync(CancellationToken ct = default) =>
        (await db.Users.Where(u => u.Active && AssignableRoles.Contains(u.Role))
            .OrderBy(u => u.Name).ToListAsync(ct))
        .Select(u => new TriageResponsible(u.Id, u.Name, u.Role)).ToList();

    /// <summary>
    /// Painel de demandas: SCs aprovadas ainda sem OC/cotação encerrada e solicitações de material em aberto.
    /// filter: TODAS | NAO_ATRIBUIDAS | MINHAS (para a fila pessoal do responsável).
    /// </summary>
    public async Task<List<TriageTicket>> ListAsync(Actor actor, string? filter, CancellationToken ct = default)
    {
        filter = (filter ?? "TODAS").Trim().ToUpperInvariant();

        var closedPrIds = await db.PurchaseOrders
            .Where(o => o.SourcePrId != null && o.Status != PurchaseOrderStatus.Cancelled)
            .Select(o => o.SourcePrId!.Value).ToListAsync(ct);

        // SCs aprovadas (prontas para cotação) e as ainda em aprovação — o comprador enxerga
        // o que está por vir e já pode designar quem dará continuidade quando a alçada liberar.
        var prs = await db.Requisitions.Include(r => r.Items)
            .Where(r => r.DeletedAt == null && !closedPrIds.Contains(r.Id)
                        && (r.Status == RequisitionStatus.Submitted || r.Status == RequisitionStatus.Approved
                            || r.Status == RequisitionStatus.InApproval))
            .OrderBy(r => r.SubmittedAt).Take(200).ToListAsync(ct);

        // gerente responsável de cada CC, para dizer de quem a SC está esperando aprovação
        var ccCodes = prs.Select(r => r.CostCenter.ToUpperInvariant()).Distinct().ToList();
        var ccManagers = await db.CostCenters
            .Where(c => c.Active && ccCodes.Contains(c.Code.ToUpper()))
            .Select(c => new { c.Code, c.ManagerName }).ToListAsync(ct);
        string PendingApprovalStatus(PurchaseRequisition r)
        {
            var manager = ccManagers.FirstOrDefault(m => m.Code.ToUpperInvariant() == r.CostCenter.ToUpperInvariant())?.ManagerName;
            return string.IsNullOrWhiteSpace(manager)
                ? "AGUARDANDO APROVAÇÃO — SEM GERENTE NO CC"
                : $"AGUARDANDO APROVAÇÃO DE {manager.ToUpperInvariant()}";
        }

        // multi-SC (V2): a SC conta como "em cotação" pelo cabeçalho OU por item agrupado
        var quotationByPr = await db.Quotations
            .Where(q => q.Status != QuotationStatus.Cancelled && q.Status != QuotationStatus.Rejected)
            .Select(q => new
            {
                q.SourcePrId, q.Number,
                ItemPrIds = q.Items.Where(i => i.SourcePrId != null).Select(i => i.SourcePrId!.Value).ToList(),
            }).ToListAsync(ct);
        var inQuotation = new Dictionary<Guid, string>();
        foreach (var x in quotationByPr)
        {
            inQuotation.TryAdd(x.SourcePrId, x.Number);
            foreach (var pid in x.ItemPrIds) inQuotation.TryAdd(pid, x.Number);
        }

        // tamanho do produto (grade de EPI/fardamento) para a lista por item
        var sizeByItem = await db.CatalogItems.Where(c => c.Size != null)
            .Select(c => new { c.Id, c.Size }).ToDictionaryAsync(x => x.Id, x => x.Size, ct);

        // situação única do fluxo de compras, a mesma que o solicitante vê
        var prIds = prs.Select(r => r.Id).ToList();
        var quotations = await db.Quotations.Include(q => q.Items)
            .Where(q => prIds.Contains(q.SourcePrId)
                        || q.Items.Any(i => i.SourcePrId != null && prIds.Contains(i.SourcePrId.Value)))
            .OrderByDescending(q => q.CreatedAt).ToListAsync(ct);
        var orders = await db.PurchaseOrders.Include(o => o.Items)
            .Where(o => o.SourcePrId != null && prIds.Contains(o.SourcePrId!.Value))
            .OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
        ProcessStatusView ProcessOf(PurchaseRequisition r) => ProcessStatus.Of(
            r, quotations.FirstOrDefault(q => q.CoversPr(r.Id)),
            orders.FirstOrDefault(o => o.SourcePrId == r.Id));

        var tickets = prs.Select(r => new TriageTicket(
            "SC", r.Id, r.Number, r.CostCenter, r.RequesterLabel,
            string.Join(" · ", r.Items.OrderBy(i => i.Sequence).Take(3)
                .Select(i => $"{i.Quantity:0.##}× {i.Description}")),
            r.TotalEstimatedValue, r.SubmittedAt ?? r.CreatedAt,
            r.Status == RequisitionStatus.InApproval ? PendingApprovalStatus(r)
                : inQuotation.TryGetValue(r.Id, out var qn) ? $"EM COTAÇÃO ({qn})" : "AGUARDANDO COMPRADOR",
            r.AssignedToId, r.AssignedToLabel, r.AssignedByLabel, r.AssignedAt, ProcessOf(r),
            r.Priority, r.NeededBy, r.Justification,
            r.Items.OrderBy(i => i.Sequence).Select(i => new TriageTicketItem(
                i.Id, i.Sequence, i.CatalogCode, i.Description,
                sizeByItem.GetValueOrDefault(i.CatalogItemId ?? Guid.Empty),
                i.Quantity, i.UnitOfMeasure, i.Notes)).ToList(),
            r.UrgencyReason, r.UrgencyImpact,
            r.PriorityChangedByLabel, r.PriorityChangeReason)).ToList();

        var mrs = await db.MaterialRequisitions.Include(r => r.Items)
            .Where(r => r.Status == MaterialRequisitionStatus.Submitted
                        || r.Status == MaterialRequisitionStatus.PurchaseRoute)
            .OrderBy(r => r.CreatedAt).Take(200).ToListAsync(ct);

        tickets.AddRange(mrs.Select(r => new TriageTicket(
            "MATERIAL", r.Id, r.Number, r.CostCenter, r.RequesterLabel,
            string.Join(" · ", r.Items.Take(3).Select(i => $"{i.Quantity:0.##}× {i.Description}")),
            null, r.CreatedAt,
            r.Status == MaterialRequisitionStatus.PurchaseRoute ? "ROTA DE COMPRA" : "AGUARDANDO ALMOXARIFADO",
            r.AssignedToId, r.AssignedToLabel, r.AssignedByLabel, r.AssignedAt,
            r.Status == MaterialRequisitionStatus.PurchaseRoute
                ? new ProcessStatusView("ROTA_COMPRA", "Rota de compra", "warn", "Sem saldo no almoxarifado: segue para compra.")
                : new ProcessStatusView("PENDENTE", "Pendente", "", "Aguardando o almoxarifado atender."),
            "NORMAL", null, r.Notes,
            r.Items.Select((i, idx) => new TriageTicketItem(
                i.Id, idx + 1, i.CatalogCode, i.Description,
                sizeByItem.GetValueOrDefault(i.CatalogItemId),
                i.Quantity, i.UnitOfMeasure, null)).ToList())));

        tickets = filter switch
        {
            "NAO_ATRIBUIDAS" => tickets.Where(t => t.AssignedToId is null).ToList(),
            "MINHAS" => tickets.Where(t => t.AssignedToId == actor.Id).ToList(),
            _ => tickets,
        };
        return tickets.OrderBy(t => t.AssignedToId is not null).ThenBy(t => t.OpenedAt).ToList();
    }

    /// <summary>
    /// Alteração de prioridade pela triagem (V2-P3): sempre com justificativa. Subir para
    /// URGENTE exige também o impacto (mesma régua do PR-ERR-050); voltar a NORMAL limpa a urgência.
    /// </summary>
    public async Task<(PurchaseRequisition? pr, UserError? error)> ChangePriorityAsync(
        Actor actor, Guid prId, string priority, string? reason, string? impact, CancellationToken ct = default)
    {
        if (!CanTriage(actor.Role))
            return (null, new("TRI-ERR-900", "Seu papel não altera a prioridade das demandas."));
        priority = priority.Trim().ToUpperInvariant();
        if (priority is not ("NORMAL" or "URGENT"))
            return (null, new("TRI-ERR-030", "Prioridade inválida: use NORMAL ou URGENT."));
        if (string.IsNullOrWhiteSpace(reason))
            return (null, new("TRI-ERR-031", "A alteração de prioridade exige justificativa."));

        var pr = await db.Requisitions.SingleOrDefaultAsync(r => r.Id == prId && r.DeletedAt == null, ct);
        if (pr is null) return (null, new("TRI-ERR-404", "Demanda não encontrada."));
        if (pr.Status is not (RequisitionStatus.Submitted or RequisitionStatus.Approved
                              or RequisitionStatus.InApproval))
            return (null, new("TRI-ERR-020", "Só demandas em andamento têm a prioridade alterada."));

        if (priority == "URGENT")
        {
            if (string.IsNullOrWhiteSpace(impact))
                return (null, new("TRI-ERR-031",
                    "Para tornar a demanda URGENTE informe também o impacto de não comprar (mesma régua da SC urgente)."));
            pr.UrgencyReason = reason.Trim();
            pr.UrgencyImpact = impact.Trim();
        }
        else
        {
            pr.UrgencyReason = null;
            pr.UrgencyImpact = null;
        }
        pr.Priority = priority;
        pr.PriorityChangedByLabel = actor.Label;
        pr.PriorityChangedAt = clock.GetUtcNow();
        pr.PriorityChangeReason = reason.Trim();
        pr.UpdatedAt = clock.GetUtcNow();
        pr.Version += 1;
        await db.SaveChangesAsync(ct);
        return (pr, null);
    }

    /// <summary>Designa (ou redesigna) o responsável pela continuidade da demanda.</summary>
    public async Task<(TriageTicket? ticket, UserError? error)> AssignAsync(
        Actor actor, string kind, Guid id, Guid? responsibleId, CancellationToken ct = default)
    {
        if (!CanTriage(actor.Role))
            return (null, new("TRI-ERR-900", "Seu papel não distribui demandas."));

        User? responsible = null;
        if (responsibleId is not null)
        {
            responsible = await db.Users.SingleOrDefaultAsync(u => u.Id == responsibleId && u.Active, ct);
            if (responsible is null || !AssignableRoles.Contains(responsible.Role))
                return (null, new("TRI-ERR-010",
                    "Responsável inválido: escolha um comprador, gestor de suprimentos ou almoxarife ativo."));
        }

        var now = clock.GetUtcNow();
        switch (kind.Trim().ToUpperInvariant())
        {
            case "SC":
            {
                var pr = await db.Requisitions.Include(r => r.Items)
                    .SingleOrDefaultAsync(r => r.Id == id && r.DeletedAt == null, ct);
                if (pr is null) return (null, new("TRI-ERR-404", "Demanda não encontrada."));
                if (pr.Status is not (RequisitionStatus.Submitted or RequisitionStatus.Approved
                                      or RequisitionStatus.InApproval))
                    return (null, new("TRI-ERR-020", "Só solicitações enviadas (ou já aprovadas) entram na triagem."));
                pr.AssignedToId = responsible?.Id;
                pr.AssignedToLabel = responsible?.Name;
                pr.AssignedById = responsible is null ? null : actor.Id;
                pr.AssignedByLabel = responsible is null ? null : actor.Label;
                pr.AssignedAt = responsible is null ? null : now;
                pr.UpdatedAt = now;
                pr.Version += 1;
                await db.SaveChangesAsync(ct);
                return (new TriageTicket("SC", pr.Id, pr.Number, pr.CostCenter, pr.RequesterLabel,
                    string.Join(" · ", pr.Items.OrderBy(i => i.Sequence).Take(3).Select(i => $"{i.Quantity:0.##}× {i.Description}")),
                    pr.TotalEstimatedValue, pr.SubmittedAt ?? pr.CreatedAt,
                    pr.Status == RequisitionStatus.InApproval ? "AGUARDANDO APROVAÇÃO" : "AGUARDANDO COMPRADOR",
                    pr.AssignedToId, pr.AssignedToLabel, pr.AssignedByLabel, pr.AssignedAt), null);
            }
            case "MATERIAL":
            {
                var mr = await db.MaterialRequisitions.Include(r => r.Items)
                    .SingleOrDefaultAsync(r => r.Id == id, ct);
                if (mr is null) return (null, new("TRI-ERR-404", "Demanda não encontrada."));
                if (mr.Status is MaterialRequisitionStatus.Cancelled or MaterialRequisitionStatus.Fulfilled)
                    return (null, new("TRI-ERR-020", "Esta solicitação já foi encerrada."));
                mr.AssignedToId = responsible?.Id;
                mr.AssignedToLabel = responsible?.Name;
                mr.AssignedById = responsible is null ? null : actor.Id;
                mr.AssignedByLabel = responsible is null ? null : actor.Label;
                mr.AssignedAt = responsible is null ? null : now;
                mr.UpdatedAt = now;
                mr.Version += 1;
                await db.SaveChangesAsync(ct);
                return (new TriageTicket("MATERIAL", mr.Id, mr.Number, mr.CostCenter, mr.RequesterLabel,
                    string.Join(" · ", mr.Items.Take(3).Select(i => $"{i.Quantity:0.##}× {i.Description}")),
                    null, mr.CreatedAt, "AGUARDANDO ALMOXARIFADO",
                    mr.AssignedToId, mr.AssignedToLabel, mr.AssignedByLabel, mr.AssignedAt), null);
            }
            default:
                return (null, new("TRI-ERR-010", "Tipo de demanda inválido: use SC ou MATERIAL."));
        }
    }
}
