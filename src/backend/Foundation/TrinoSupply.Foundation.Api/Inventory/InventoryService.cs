using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Inventory;

/// <summary>
/// Serviço do Estoque (MMS-004 MVP). Único ponto de efeito sobre StockBalance (INV-IV-01):
/// saldo é sempre derivado de movimentações documentadas; saída nunca gera saldo negativo (MMS-RG-04);
/// documentos são imutáveis (correção por movimento inverso — evolução).
/// </summary>
public class InventoryService(AppDbContext db, TimeProvider clock)
{
    public static bool CanOperate(string role) =>
        role is Roles.WarehouseOperator or Roles.WarehouseSupervisor or Roles.SupplyManager or Roles.SystemAdministrator;

    public static bool CanView(string role) => CanOperate(role) || role == Roles.Auditor;

    // ---- locais -------------------------------------------------------------
    public Task<List<StorageLocation>> LocationsAsync(bool onlyActive = true, CancellationToken ct = default)
    {
        var q = db.StorageLocations.AsQueryable();
        if (onlyActive) q = q.Where(l => l.Active);
        return q.OrderBy(l => l.Code).ToListAsync(ct);
    }

    public async Task<(StorageLocation? location, UserError? error)> CreateLocationAsync(
        Guid actorId, string code, string name, CancellationToken ct = default)
    {
        code = code.Trim().ToUpperInvariant();
        if (code.Length < 2) return (null, new("IV-ERR-061", "Informe o código do local (mín. 2 caracteres)."));
        if (name.Trim().Length < 3) return (null, new("IV-ERR-061", "Informe o nome do local."));
        if (await db.StorageLocations.AnyAsync(l => l.Code == code, ct))
            return (null, new("IV-ERR-061", "Já existe um local com este código."));
        var location = new StorageLocation { Code = code, Name = name.Trim(), CreatedAt = clock.GetUtcNow(), CreatedBy = actorId };
        db.StorageLocations.Add(location);
        await db.SaveChangesAsync(ct);
        return (location, null);
    }

    // ---- consulta -----------------------------------------------------------
    public async Task<List<(StockBalance balance, Catalog.CatalogItem item, StorageLocation location)>> BalancesAsync(
        Guid? itemId, Guid? locationId, CancellationToken ct = default)
    {
        var query =
            from b in db.StockBalances
            join i in db.CatalogItems on b.CatalogItemId equals i.Id
            join l in db.StorageLocations on b.LocationId equals l.Id
            select new { b, i, l };
        if (itemId is not null) query = query.Where(x => x.b.CatalogItemId == itemId);
        if (locationId is not null) query = query.Where(x => x.b.LocationId == locationId);
        var rows = await query.OrderBy(x => x.i.Description).ThenBy(x => x.l.Code).Take(500).ToListAsync(ct);
        return rows.Select(x => (x.b, x.i, x.l)).ToList();
    }

    public Task<List<StockMovement>> MovementsAsync(Guid? itemId, Guid? locationId, CancellationToken ct = default)
    {
        var q = db.StockMovements.AsQueryable();
        if (itemId is not null) q = q.Where(m => m.CatalogItemId == itemId);
        if (locationId is not null) q = q.Where(m => m.LocationId == locationId);
        return q.OrderByDescending(m => m.PerformedAt).ThenByDescending(m => m.Id).Take(200).ToListAsync(ct);
    }

    /// <summary>Disponível por item (soma dos locais), para validação do MMS-003 (MMS-RG-09).</summary>
    public async Task<decimal> AvailableAsync(Guid catalogItemId, Guid? locationId = null, CancellationToken ct = default)
    {
        var q = db.StockBalances.Where(b => b.CatalogItemId == catalogItemId);
        if (locationId is not null) q = q.Where(b => b.LocationId == locationId);
        return await q.SumAsync(b => b.TotalQty - b.ReservedQty, ct);
    }

    // ---- movimentações (único caminho de efeito sobre saldo) ----------------
    public async Task<(StockMovement? movement, UserError? error)> RegisterEntryAsync(
        Actor actor, Guid catalogItemId, Guid locationId, decimal quantity, MovementOrigin origin,
        string originReference, CancellationToken ct = default)
    {
        if (origin is not (MovementOrigin.Receiving or MovementOrigin.Return or MovementOrigin.InitialLoad))
            return (null, new("IV-ERR-010", "Origem inválida para entrada."));
        return await ApplyAsync(actor, MovementType.Entry, origin, originReference, catalogItemId, locationId, quantity, null, ct);
    }

    public async Task<(StockMovement? movement, UserError? error)> RegisterIssueAsync(
        Actor actor, Guid catalogItemId, Guid locationId, decimal quantity, MovementOrigin origin,
        string originReference, Guid? materialRequisitionId = null, CancellationToken ct = default)
    {
        if (origin is not (MovementOrigin.Fulfillment or MovementOrigin.Consumption))
            return (null, new("IV-ERR-011", "Origem inválida para saída."));
        return await ApplyAsync(actor, MovementType.Issue, origin, originReference, catalogItemId, locationId, quantity, materialRequisitionId, ct);
    }

    private async Task<(StockMovement? movement, UserError? error)> ApplyAsync(
        Actor actor, MovementType type, MovementOrigin origin, string originReference,
        Guid catalogItemId, Guid locationId, decimal quantity, Guid? materialRequisitionId, CancellationToken ct)
    {
        if (quantity <= 0) return (null, new("IV-ERR-009", "A quantidade deve ser maior que zero."));
        if (string.IsNullOrWhiteSpace(originReference))
            return (null, new("IV-ERR-002", "Informe o documento de origem da movimentação."));

        var item = await db.CatalogItems.SingleOrDefaultAsync(i => i.Id == catalogItemId, ct);
        if (item is null) return (null, new("IV-ERR-010", "Item inexistente no catálogo."));
        if (!item.Active && type == MovementType.Entry && origin != MovementOrigin.Return)
            return (null, new("IV-ERR-010", "Item inativo no catálogo: nova entrada bloqueada (devolução é permitida)."));

        var location = await db.StorageLocations.SingleOrDefaultAsync(l => l.Id == locationId && l.Active, ct);
        if (location is null) return (null, new("IV-ERR-060", "Local inválido ou inativo."));

        var balance = await db.StockBalances
            .SingleOrDefaultAsync(b => b.CatalogItemId == catalogItemId && b.LocationId == locationId, ct);
        var before = balance?.TotalQty ?? 0;

        if (type == MovementType.Issue && before - (balance?.ReservedQty ?? 0) < quantity)
            return (null, new("IV-ERR-020", $"Saldo disponível insuficiente: disponível {(before - (balance?.ReservedQty ?? 0)):0.##}, solicitado {quantity:0.##}."));

        var now = clock.GetUtcNow();
        var after = type == MovementType.Entry ? before + quantity : before - quantity;

        var movement = new StockMovement
        {
            Number = $"MOV-{now.Year}-{await NextSeqAsync(ct):000000}",
            Type = type,
            Origin = origin,
            OriginReference = originReference.Trim(),
            CatalogItemId = item.Id,
            ItemCode = item.Code,
            ItemDescription = item.Description,
            UnitOfMeasure = item.UnitOfMeasure,
            LocationId = locationId,
            Quantity = quantity,
            BalanceBefore = before,
            BalanceAfter = after,
            MaterialRequisitionId = materialRequisitionId,
            PerformedBy = actor.Id,
            PerformedByLabel = actor.Label,
            PerformedAt = now,
        };
        db.StockMovements.Add(movement);

        if (balance is null)
        {
            db.StockBalances.Add(new StockBalance
            {
                CatalogItemId = catalogItemId,
                LocationId = locationId,
                TotalQty = after,
                LastMovementId = movement.Id,
                LastMovementAt = now,
            });
        }
        else
        {
            balance.TotalQty = after;
            balance.LastMovementId = movement.Id;
            balance.LastMovementAt = now;
            balance.Version += 1;
        }

        await db.SaveChangesAsync(ct);
        return (movement, null);
    }

    private async Task<long> NextSeqAsync(CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return await db.StockMovements.LongCountAsync(ct) + 1;
        return await db.Database
            .SqlQueryRaw<long>("SELECT nextval('materials.mov_number_seq') AS \"Value\"")
            .SingleAsync(ct);
    }
}
