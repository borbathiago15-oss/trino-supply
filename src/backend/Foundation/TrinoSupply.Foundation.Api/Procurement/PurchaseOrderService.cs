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
        db.PurchaseOrders.Include(o => o.Items).Include(o => o.Invoices)
            .OrderByDescending(o => o.CreatedAt).Take(100).ToListAsync(ct);

    public Task<PurchaseOrder?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.PurchaseOrders.Include(o => o.Items).Include(o => o.Invoices)
            .SingleOrDefaultAsync(o => o.Id == id, ct);

    /// <summary>
    /// Demandas do comprador: solicitações de compra ainda sem cotação nem pedido. O faltante do
    /// almoxarifado deixou de aparecer aqui como item solto — desde a revisão do módulo de estoque
    /// (2026-08-26) ele vira uma solicitação de compra própria, em nome de quem pediu o material.
    /// </summary>
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

        return (prs, []);
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
    /// <summary>
    /// Registra a OC feita no ERP (número, data e anexo ficam no endpoint de upload).
    /// A data é a base do lead time "aprovação → OC" (revisão de telas 2026-08-26).
    /// </summary>
    public async Task<(PurchaseOrder? order, UserError? error)> RegisterErpOrderAsync(
        Actor actor, Guid id, string? erpNumber, DateOnly? issuedOn, CancellationToken ct = default)
    {
        var order = await LoadAsync(id, ct);
        if (order is null) return (null, new("PO-ERR-404", "Pedido não encontrado."));
        if (order.Status == PurchaseOrderStatus.Cancelled)
            return (null, new("PO-ERR-040", "Pedido cancelado não recebe OC."));
        if (string.IsNullOrWhiteSpace(erpNumber))
            return (null, new("PO-ERR-050", "Informe o número da OC gerada no ERP."));

        var numero = erpNumber.Trim();
        if (numero.Length > 30)
            return (null, new("PO-ERR-050", "O número da OC tem no máximo 30 caracteres."));
        // a OC do SENIOR é única no sistema (RFQ-ERR-041): o caminho da cotação já
        // garantia isso pelo número do pedido, mas por aqui dava para repetir
        if (await db.PurchaseOrders.AnyAsync(o => o.Id != order.Id && o.ErpNumber == numero, ct))
            return (null, new("PO-ERR-050", $"A OC {numero} já está registrada em outro pedido."));

        order.ErpNumber = numero;
        order.ErpIssuedOn = issuedOn ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        Touch(order);
        await db.SaveChangesAsync(ct);
        return (order, null);
    }

    /// <summary>Nota fiscal do faturamento: uma OC pode receber mais de uma.</summary>
    public async Task<(PurchaseOrderInvoice? invoice, UserError? error)> AddInvoiceAsync(
        Actor actor, Guid id, string? number, DateOnly? issuedOn, decimal? value, CancellationToken ct = default)
    {
        var order = await LoadAsync(id, ct);
        if (order is null) return (null, new("PO-ERR-404", "Pedido não encontrado."));
        if (order.Status == PurchaseOrderStatus.Cancelled)
            return (null, new("PO-ERR-040", "Pedido cancelado não recebe faturamento."));
        if (string.IsNullOrWhiteSpace(number))
            return (null, new("PO-ERR-051", "Informe o número da nota fiscal."));
        if (order.ErpNumber is null)
            return (null, new("PO-ERR-052", "Registre primeiro a OC do ERP para depois lançar a nota fiscal."));
        if (value is < 0) return (null, new("PO-ERR-053", "O valor da nota não pode ser negativo."));

        var now = clock.GetUtcNow();
        var invoice = new PurchaseOrderInvoice
        {
            OrderId = order.Id, Number = number.Trim(),
            IssuedOn = issuedOn ?? DateOnly.FromDateTime(now.UtcDateTime),
            Value = value, CreatedBy = actor.Id, CreatedByLabel = actor.Label, CreatedAt = now,
        };
        db.PurchaseOrderInvoices.Add(invoice);
        if (order.Status == PurchaseOrderStatus.Issued) order.Status = PurchaseOrderStatus.Invoiced;
        Touch(order);
        await db.SaveChangesAsync(ct);
        return (invoice, null);
    }

    public record ReceiptLine(Guid ItemId, decimal Quantity, decimal? Rejected = null);

    /// <summary>
    /// Confirmação de entrega em três desfechos (revisão de telas 2026-08-26): total, parcial
    /// (o que faltou continua pendente) ou cancelamento do saldo que não vai chegar.
    /// </summary>
    public async Task<(PurchaseOrder? order, UserError? error)> RegisterDeliveryAsync(
        Actor actor, Guid id, Guid? locationId, IReadOnlyList<ReceiptLine> lines,
        bool closeRemaining, string? closeReason, string? rejectReason = null, CancellationToken ct = default)
    {
        var order = await LoadAsync(id, ct);
        if (order is null) return (null, new("PO-ERR-404", "Pedido não encontrado."));
        if (order.Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Received)
            return (null, new("PO-ERR-040", "Este pedido já foi encerrado."));
        // saldo já encerrado: o que não chegou foi cancelado e o pedido não recebe mais entregas
        if (order.DeliveryCompletedAt is not null)
            return (null, new("PO-ERR-040", "A entrega deste pedido já foi encerrada."));

        var received = new List<(PurchaseOrderItem item, decimal qty)>();
        // devolução (V2-P3): o que chegou mas foi recusado — não entra no estoque nem no recebido
        var rejected = new List<(PurchaseOrderItem item, decimal qty)>();
        foreach (var line in lines.Where(l => l.Quantity > 0 || l.Rejected > 0))
        {
            var item = order.Items.SingleOrDefault(i => i.Id == line.ItemId);
            if (item is null) return (null, new("PO-ERR-054", "Item informado não pertence a este pedido."));
            var devolvida = line.Rejected ?? 0;
            if (line.Quantity < 0 || devolvida < 0)
                return (null, new("PO-ERR-055", $"{item.Description}: quantidades negativas não são aceitas."));
            var pending = item.Quantity - item.ReceivedQuantity;
            if (line.Quantity + devolvida > pending)
                return (null, new("PO-ERR-055",
                    $"{item.Description}: chegaram {line.Quantity + devolvida}, mas faltavam apenas {pending}."));
            if (line.Quantity > 0) received.Add((item, line.Quantity));
            if (devolvida > 0) rejected.Add((item, devolvida));
        }
        if (rejected.Count > 0 && string.IsNullOrWhiteSpace(rejectReason))
            return (null, new("PO-ERR-058", "Informe o motivo da devolução (qualidade, avaria, divergência…)."));
        if (received.Count == 0 && rejected.Count == 0 && !closeRemaining)
            return (null, new("PO-ERR-056", "Informe as quantidades que chegaram ou encerre o saldo pendente."));

        // o saldo de estoque vive no sistema do almoxarifado: só lançamos entrada quando o
        // local é informado (tela de Pedidos de Compra); no processo de cotação a entrega é só registro
        var comCatalogo = locationId is null
            ? []
            : received.Where(r => r.item.CatalogItemId is not null).ToList();
        if (comCatalogo.Count > 0)
        {
            if (!await db.StorageLocations.AnyAsync(l => l.Id == locationId!.Value && l.Active, ct))
                return (null, new("IV-ERR-060", "Local inválido ou inativo."));
            var ids = comCatalogo.Select(r => r.item.CatalogItemId!.Value).Distinct().ToList();
            var inactive = await db.CatalogItems
                .Where(c => ids.Contains(c.Id) && !c.Active).Select(c => c.Code).ToListAsync(ct);
            if (inactive.Count > 0)
                return (null, new("IV-ERR-010", $"Itens inativos no catálogo: {string.Join(", ", inactive)} — regularize antes de receber."));

            foreach (var (item, qty) in comCatalogo)
            {
                var (_, entryError) = await inventory.RegisterEntryAsync(
                    actor, item.CatalogItemId!.Value, locationId!.Value, qty,
                    MovementOrigin.Receiving, order.Number, ct);
                if (entryError is not null) return (null, entryError);
            }
        }

        var now = clock.GetUtcNow();
        foreach (var (item, qty) in received) item.ReceivedQuantity += qty;
        foreach (var (item, qty) in rejected)
        {
            item.RejectedQuantity += qty;
            item.RejectionReason = rejectReason!.Trim();
        }

        if (closeRemaining && order.HasPendingDelivery)
        {
            if (string.IsNullOrWhiteSpace(closeReason))
                return (null, new("PO-ERR-057", "Informe o motivo do cancelamento do saldo não entregue."));
            order.CancelReason = closeReason.Trim();
            order.Status = order.Items.Any(i => i.ReceivedQuantity > 0)
                ? PurchaseOrderStatus.PartiallyReceived      // parte chegou, o resto foi cancelado
                : PurchaseOrderStatus.Cancelled;             // nada chegou
            order.DeliveryCompletedAt = now;
        }
        else if (!order.HasPendingDelivery)
        {
            order.Status = PurchaseOrderStatus.Received;
            order.DeliveryCompletedAt = now;
        }
        else
        {
            order.Status = PurchaseOrderStatus.PartiallyReceived;
        }

        order.ReceivedBy = actor.Id;
        order.ReceivedByLabel = actor.Label;
        order.ReceivedAt = now;
        Touch(order);
        await db.SaveChangesAsync(ct);
        return (order, null);
    }

    private Task<PurchaseOrder?> LoadAsync(Guid id, CancellationToken ct) =>
        db.PurchaseOrders.Include(o => o.Items).Include(o => o.Invoices)
            .SingleOrDefaultAsync(o => o.Id == id, ct);

    private void Touch(PurchaseOrder order)
    {
        order.UpdatedAt = clock.GetUtcNow();
        order.Version += 1;
    }

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
        if (order.Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Received)
            return (null, new("PO-ERR-040", "Este pedido já foi encerrado."));
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
