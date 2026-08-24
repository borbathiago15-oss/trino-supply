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
    Guid? AssignedToId, string? AssignedToLabel, string? AssignedByLabel, DateTimeOffset? AssignedAt);

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

        var quotationByPr = await db.Quotations
            .Where(q => q.Status != QuotationStatus.Cancelled && q.Status != QuotationStatus.Rejected)
            .Select(q => new { q.SourcePrId, q.Number }).ToListAsync(ct);
        var inQuotation = quotationByPr.ToDictionary(x => x.SourcePrId, x => x.Number);

        var tickets = prs.Select(r => new TriageTicket(
            "SC", r.Id, r.Number, r.CostCenter, r.RequesterLabel,
            string.Join(" · ", r.Items.OrderBy(i => i.Sequence).Take(3)
                .Select(i => $"{i.Quantity:0.##}× {i.Description}")),
            r.TotalEstimatedValue, r.SubmittedAt ?? r.CreatedAt,
            r.Status == RequisitionStatus.InApproval ? PendingApprovalStatus(r)
                : inQuotation.TryGetValue(r.Id, out var qn) ? $"EM COTAÇÃO ({qn})" : "AGUARDANDO COMPRADOR",
            r.AssignedToId, r.AssignedToLabel, r.AssignedByLabel, r.AssignedAt)).ToList();

        var mrs = await db.MaterialRequisitions.Include(r => r.Items)
            .Where(r => r.Status == MaterialRequisitionStatus.Submitted
                        || r.Status == MaterialRequisitionStatus.PurchaseRoute)
            .OrderBy(r => r.CreatedAt).Take(200).ToListAsync(ct);

        tickets.AddRange(mrs.Select(r => new TriageTicket(
            "MATERIAL", r.Id, r.Number, r.CostCenter, r.RequesterLabel,
            string.Join(" · ", r.Items.Take(3).Select(i => $"{i.Quantity:0.##}× {i.Description}")),
            null, r.CreatedAt,
            r.Status == MaterialRequisitionStatus.PurchaseRoute ? "ROTA DE COMPRA" : "AGUARDANDO ALMOXARIFADO",
            r.AssignedToId, r.AssignedToLabel, r.AssignedByLabel, r.AssignedAt)));

        tickets = filter switch
        {
            "NAO_ATRIBUIDAS" => tickets.Where(t => t.AssignedToId is null).ToList(),
            "MINHAS" => tickets.Where(t => t.AssignedToId == actor.Id).ToList(),
            _ => tickets,
        };
        return tickets.OrderBy(t => t.AssignedToId is not null).ThenBy(t => t.OpenedAt).ToList();
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
