using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Inventory;
using TrinoSupply.Foundation.Api.Materials;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

public record PoItemInput(string Description, decimal Quantity, string? UnitOfMeasure, decimal? UnitPrice, Guid? CatalogItemId);

/// <summary>
/// Serviço do Pedido de Compra (PO-001 MVP): EMITIDO → RECEBIDO/CANCELADO (PO-BR-004);
/// recebimento gera entradas de estoque via InventoryService para itens de catálogo (PO-BR-005),
/// preservando a invariante de saldo somente por movimentação (INV-IV-01).
/// </summary>
public class PurchaseOrderService(AppDbContext db, InventoryService inventory, TimeProvider clock)
{
    public static bool CanManage(string role) =>
        role is Roles.PurchasingOfficer or Roles.SupplyManager or Roles.SystemAdministrator;

    public static bool CanView(string role) => CanManage(role) || role == Roles.Auditor;

    public Task<List<PurchaseOrder>> ListAsync(CancellationToken ct = default) =>
        db.PurchaseOrders.Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt).Take(100).ToListAsync(ct);

    /// <summary>Demandas do comprador: requisições aprovadas sem pedido + itens MR em rota de compra.</summary>
    public async Task<(List<PurchaseRequisition> approvedPrs, List<(MaterialRequisition mr, MaterialRequisitionItem item)> mrItems)>
        DemandsAsync(CancellationToken ct = default)
    {
        var linkedPrIds = await db.PurchaseOrders
            .Where(o => o.SourcePrId != null && o.Status != PurchaseOrderStatus.Cancelled)
            .Select(o => o.SourcePrId!.Value).ToListAsync(ct);
        var quotedPrIds = await db.Quotations
            .Where(q => q.Status != QuotationStatus.Cancelled && q.Status != QuotationStatus.Rejected)
            .Select(q => q.SourcePrId).ToListAsync(ct);
        var prs = await db.Requisitions.Include(r => r.Items)
            .Where(r => (r.Status == RequisitionStatus.Submitted || r.Status == RequisitionStatus.Approved)
                        && !linkedPrIds.Contains(r.Id) && !quotedPrIds.Contains(r.Id))
            .OrderBy(r => r.DecidedAt).Take(100).ToListAsync(ct);

        var mrs = await db.MaterialRequisitions.Include(r => r.Items)
            .Where(r => r.Status != MaterialRequisitionStatus.Cancelled
                        && r.Items.Any(i => i.Status == MaterialItemStatus.PurchaseRoute))
            .OrderBy(r => r.CreatedAt).Take(100).ToListAsync(ct);
        var mrItems = mrs
            .SelectMany(mr => mr.Items
                .Where(i => i.Status == MaterialItemStatus.PurchaseRoute)
                .Select(i => (mr, i)))
            .ToList();
        return (prs, mrItems);
    }

    public async Task<(PurchaseOrder? order, UserError? error)> CreateAsync(
        Actor actor, Guid supplierId, string? notes, IReadOnlyList<PoItemInput> items, Guid? sourcePrId,
        CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == supplierId && s.Active, ct);
        if (supplier is null) return (null, new("PO-ERR-020", "Fornecedor inexistente ou inativo."));

        var inputs = items.ToList();
        // RFQ-BR-010: PR aprovada segue exclusivamente pelo processo de cotação —
        // a conversão direta requisição → OC foi desativada.
        if (sourcePrId is not null)
            return (null, new("PO-ERR-023",
                "Emissão a partir de requisição exige o processo de cotação (RFQ-001): abra a cotação, conclua as aprovações e emita a OC pelo processo."));

        if (inputs.Count == 0) return (null, new("PO-ERR-030", "Inclua ao menos um item no pedido."));
        if (inputs.Any(i => i.Quantity <= 0 || i.UnitPrice is < 0))
            return (null, new("PO-ERR-010", "Quantidades devem ser positivas e preços não podem ser negativos."));
        if (inputs.Any(i => string.IsNullOrWhiteSpace(i.Description)))
            return (null, new("PO-ERR-030", "Descreva todos os itens do pedido."));

        var catalogIds = inputs.Where(i => i.CatalogItemId is not null).Select(i => i.CatalogItemId!.Value).Distinct().ToList();
        var catalogCodes = await db.CatalogItems.Where(c => catalogIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Code, ct);

        var now = clock.GetUtcNow();
        var order = new PurchaseOrder
        {
            Number = $"PO-{now.Year}-{await NextSeqAsync(ct):000000}",
            SupplierId = supplier.Id,
            SupplierName = supplier.TradeName ?? supplier.LegalName,
            SourcePrId = null,
            SourcePrNumber = null,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            IssuedBy = actor.Id,
            IssuedByLabel = actor.Label,
            CreatedAt = now,
            UpdatedAt = now,
        };
        foreach (var input in inputs)
        {
            order.Items.Add(new PurchaseOrderItem
            {
                Description = input.Description.Trim(),
                UnitOfMeasure = string.IsNullOrWhiteSpace(input.UnitOfMeasure) ? "UN" : input.UnitOfMeasure.Trim().ToUpperInvariant(),
                Quantity = input.Quantity,
                UnitPrice = input.UnitPrice,
                CatalogItemId = input.CatalogItemId,
                CatalogCode = input.CatalogItemId is not null ? catalogCodes.GetValueOrDefault(input.CatalogItemId.Value) : null,
                CreatedAt = now,
            });
        }
        order.TotalValue = order.Items.Sum(i => (i.UnitPrice ?? 0) * i.Quantity);
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync(ct);
        return (order, null);
    }

    /// <summary>Recebimento: entradas de estoque para itens de catálogo (PO-BR-005/006).</summary>
    public async Task<(PurchaseOrder? order, UserError? error)> ReceiveAsync(
        Actor actor, Guid id, Guid locationId, CancellationToken ct = default)
    {
        var order = await db.PurchaseOrders.Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return (null, new("PO-ERR-404", "Pedido não encontrado."));
        if (order.Status != PurchaseOrderStatus.Issued)
            return (null, new("PO-ERR-040", "Somente pedidos emitidos podem ser recebidos."));

        var catalogItems = order.Items.Where(i => i.CatalogItemId is not null).ToList();
        if (catalogItems.Count > 0)
        {
            if (!await db.StorageLocations.AnyAsync(l => l.Id == locationId && l.Active, ct))
                return (null, new("IV-ERR-060", "Local inválido ou inativo."));
            var ids = catalogItems.Select(i => i.CatalogItemId!.Value).Distinct().ToList();
            var inactive = await db.CatalogItems
                .Where(c => ids.Contains(c.Id) && !c.Active).Select(c => c.Code).ToListAsync(ct);
            if (inactive.Count > 0)
                return (null, new("IV-ERR-010", $"Itens inativos no catálogo: {string.Join(", ", inactive)} — regularize antes de receber."));

            foreach (var item in catalogItems)
            {
                var (_, entryError) = await inventory.RegisterEntryAsync(
                    actor, item.CatalogItemId!.Value, locationId, item.Quantity,
                    MovementOrigin.Receiving, order.Number, ct);
                if (entryError is not null) return (null, entryError);
            }
        }

        order.Status = PurchaseOrderStatus.Received;
        order.ReceivedBy = actor.Id;
        order.ReceivedByLabel = actor.Label;
        order.ReceivedAt = clock.GetUtcNow();
        order.UpdatedAt = order.ReceivedAt.Value;
        order.Version += 1;
        await db.SaveChangesAsync(ct);
        return (order, null);
    }

    public async Task<(PurchaseOrder? order, UserError? error)> CancelAsync(
        Actor actor, Guid id, string? reason, CancellationToken ct = default)
    {
        var order = await db.PurchaseOrders.Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return (null, new("PO-ERR-404", "Pedido não encontrado."));
        if (order.Status != PurchaseOrderStatus.Issued)
            return (null, new("PO-ERR-040", "Somente pedidos emitidos podem ser cancelados."));
        if (string.IsNullOrWhiteSpace(reason))
            return (null, new("PO-ERR-041", "Informe o motivo do cancelamento."));

        order.Status = PurchaseOrderStatus.Cancelled;
        order.CancelReason = reason.Trim();
        order.UpdatedAt = clock.GetUtcNow();
        order.Version += 1;
        await db.SaveChangesAsync(ct);
        return (order, null);
    }

    private async Task<long> NextSeqAsync(CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return await db.PurchaseOrders.LongCountAsync(ct) + 1;
        return await db.Database
            .SqlQueryRaw<long>("SELECT nextval('procurement.po_number_seq') AS \"Value\"")
            .SingleAsync(ct);
    }
}
