using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Inventory;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Materials;

public record MaterialItemInput(Guid CatalogItemId, decimal Quantity);

/// <summary>
/// Serviço da Solicitação de Material (MMS-003 MVP): itens do catálogo (MMS-002),
/// atendimento com baixa via Estoque (MMS-004, somente disponível — MMS-RG-09),
/// rota mista por item: sem saldo → rota de compra (MMS-RG-09/PR-001).
/// </summary>
public class MaterialRequisitionService(AppDbContext db, CatalogService catalog, InventoryService inventory, TimeProvider clock)
{
    public static bool CanRequest(string role) =>
        role is Roles.Requester or Roles.SupplyManager or Roles.SystemAdministrator;

    public static bool CanFulfill(string role) => InventoryService.CanOperate(role);

    public static bool CanSeeAll(string role) =>
        CanFulfill(role) || role is Roles.Auditor;

    public async Task<List<MaterialRequisition>> ListAsync(Actor actor, bool queueOnly, CancellationToken ct = default)
    {
        var query = db.MaterialRequisitions.Include(r => r.Items).AsQueryable();
        if (queueOnly) query = query.Where(r => r.Status == MaterialRequisitionStatus.Submitted);
        else if (!CanSeeAll(actor.Role)) query = query.Where(r => r.RequesterId == actor.Id);
        return await query.OrderByDescending(r => r.CreatedAt).Take(100).ToListAsync(ct);
    }

    public async Task<(MaterialRequisition? mr, UserError? error)> CreateAsync(
        Actor actor, string costCenter, string? notes, IReadOnlyList<MaterialItemInput> items, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(costCenter))
            return (null, new("MR-ERR-021", "Informe o centro de custo."));
        if (items.Count == 0)
            return (null, new("MR-ERR-030", "Inclua ao menos um item do catálogo."));
        if (items.Any(i => i.Quantity <= 0))
            return (null, new("MR-ERR-010", "As quantidades devem ser maiores que zero."));

        var (catalogItems, catalogError) = await catalog.ResolveForRequisitionAsync(
            items.Select(i => i.CatalogItemId).Distinct().ToList(), ct);
        if (catalogError is not null) return (null, catalogError);

        var now = clock.GetUtcNow();
        var mr = new MaterialRequisition
        {
            Number = $"MR-{now.Year}-{await NextSeqAsync(ct):000000}",
            CostCenter = costCenter.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            RequesterId = actor.Id,
            RequesterLabel = actor.Label,
            CreatedAt = now,
            UpdatedAt = now,
        };
        foreach (var input in items)
        {
            var c = catalogItems![input.CatalogItemId];
            mr.Items.Add(new MaterialRequisitionItem
            {
                CatalogItemId = c.Id,
                CatalogCode = c.Code,
                Description = c.Description,
                UnitOfMeasure = c.UnitOfMeasure,
                Quantity = input.Quantity,
                CreatedAt = now,
            });
        }
        db.MaterialRequisitions.Add(mr);
        await db.SaveChangesAsync(ct);
        return (mr, null);
    }

    /// <summary>
    /// Atendimento (rota mista por item, MMS-003): para cada item, se o disponível no local
    /// cobrir a quantidade, gera a saída vinculada; senão o item vai para rota de compra.
    /// </summary>
    public async Task<(MaterialRequisition? mr, UserError? error)> FulfillAsync(
        Actor actor, Guid id, Guid locationId, CancellationToken ct = default)
    {
        var mr = await db.MaterialRequisitions.Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == id, ct);
        if (mr is null) return (null, new("MR-ERR-404", "Solicitação não encontrada."));
        if (mr.Status != MaterialRequisitionStatus.Submitted)
            return (null, new("MR-ERR-040", "A solicitação não está aguardando atendimento."));

        foreach (var item in mr.Items.Where(i => i.Status == MaterialItemStatus.Pending))
        {
            var available = await inventory.AvailableAsync(item.CatalogItemId, locationId, ct);
            if (available >= item.Quantity)
            {
                var (movement, issueError) = await inventory.RegisterIssueAsync(
                    actor, item.CatalogItemId, locationId, item.Quantity,
                    MovementOrigin.Fulfillment, mr.Number, mr.Id, ct);
                if (issueError is not null) return (null, issueError);
                item.Status = MaterialItemStatus.Fulfilled;
                item.StockMovementId = movement!.Id;
            }
            else
            {
                item.Status = MaterialItemStatus.PurchaseRoute; // demanda para PR-001 (rota mista)
            }
        }

        var fulfilled = mr.Items.Count(i => i.Status == MaterialItemStatus.Fulfilled);
        mr.Status = fulfilled == mr.Items.Count ? MaterialRequisitionStatus.Fulfilled
            : fulfilled > 0 ? MaterialRequisitionStatus.PartiallyFulfilled
            : MaterialRequisitionStatus.PurchaseRoute;
        mr.FulfilledBy = actor.Id;
        mr.FulfilledByLabel = actor.Label;
        mr.FulfilledAt = clock.GetUtcNow();
        mr.UpdatedAt = mr.FulfilledAt.Value;
        mr.Version += 1;
        await db.SaveChangesAsync(ct);
        return (mr, null);
    }

    public async Task<(MaterialRequisition? mr, UserError? error)> CancelAsync(
        Actor actor, Guid id, string? reason, CancellationToken ct = default)
    {
        var mr = await db.MaterialRequisitions.Include(r => r.Items).SingleOrDefaultAsync(r => r.Id == id, ct);
        if (mr is null || (!CanSeeAll(actor.Role) && mr.RequesterId != actor.Id))
            return (null, new("MR-ERR-404", "Solicitação não encontrada."));
        if (mr.RequesterId != actor.Id && actor.Role != Roles.SystemAdministrator)
            return (null, new("MR-ERR-001", "Somente o solicitante pode cancelar."));
        if (mr.Status != MaterialRequisitionStatus.Submitted)
            return (null, new("MR-ERR-040", "Somente solicitações aguardando atendimento podem ser canceladas."));
        if (string.IsNullOrWhiteSpace(reason))
            return (null, new("MR-ERR-030", "Informe o motivo do cancelamento."));

        mr.Status = MaterialRequisitionStatus.Cancelled;
        mr.CancelReason = reason.Trim();
        mr.UpdatedAt = clock.GetUtcNow();
        mr.Version += 1;
        await db.SaveChangesAsync(ct);
        return (mr, null);
    }

    private async Task<long> NextSeqAsync(CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return await db.MaterialRequisitions.LongCountAsync(ct) + 1;
        return await db.Database
            .SqlQueryRaw<long>("SELECT nextval('materials.mr_number_seq') AS \"Value\"")
            .SingleAsync(ct);
    }
}
