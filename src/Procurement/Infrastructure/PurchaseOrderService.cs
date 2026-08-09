using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.BuildingBlocks.Security;
using TrinoSupply.Procurement.Application;
using TrinoSupply.Procurement.Domain;
using TrinoSupply.Procurement.Infrastructure.Persistence;

namespace TrinoSupply.Procurement.Infrastructure;

/// <summary>Pedido de compra (PR-001): emissão a partir de requisição aprovada + consultas.</summary>
public sealed class PurchaseOrderService(
    ProcurementDbContext db, ITenantContext tenant, ICurrentUser currentUser, IUsageMetrics metrics, IClock clock)
    : IPurchaseOrderService
{
    public async Task<Result<Guid>> IssueFromRequisitionAsync(Guid requisitionId, string supplierCode, CancellationToken ct = default)
    {
        if (!tenant.HasTenant || string.IsNullOrWhiteSpace(currentUser.Subject))
            return Result.Failure<Guid>(new Error("purchases.no_context", "Requisição sem tenant/usuário."));

        var req = await db.Requisitions.AsNoTracking().Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == RequisitionId.From(requisitionId), ct);
        if (req is null)
            return Result.Failure<Guid>(new Error("purchases.not_found", "Requisição não encontrada."));
        if (req.Status != RequisitionStatus.Approved)
            return Result.Failure<Guid>(new Error("purchases.order.not_approved", "Só requisições aprovadas geram pedido."));

        var supplier = await db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Code == supplierCode.Trim().ToUpperInvariant(), ct);
        if (supplier is null)
            return Result.Failure<Guid>(new Error("purchases.supplier.not_found", $"Fornecedor '{supplierCode}' não encontrado."));

        var lines = req.Lines.Select(l => (l.ItemCode, l.Quantity, l.Unit));
        var order = PurchaseOrder.Issue(tenant.CompanyId, req.Id, supplier.Id, currentUser.Subject!, clock.UtcNow, lines);
        if (order.IsFailure) return Result.Failure<Guid>(order.Error);

        db.Orders.Add(order.Value);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // índice único (company, requisition_id): já existe pedido para esta requisição.
            return Result.Failure<Guid>(new Error("purchases.order.already_exists", "Esta requisição já gerou um pedido."));
        }

        metrics.Record("purchases.order.issued", tenant.CompanyId.Value.ToString());
        return Result.Success(order.Value.Id.Value);
    }

    public async Task<Result<PurchaseOrderView>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == PurchaseOrderId.From(id), ct);
        if (order is null)
            return Result.Failure<PurchaseOrderView>(new Error("purchases.order.not_found", "Pedido não encontrado."));

        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == order.SupplierId, ct);
        return Result.Success(ToView(order, supplier?.Code ?? string.Empty));
    }

    public async Task<IReadOnlyList<PurchaseOrderView>> ListAsync(CancellationToken ct = default)
    {
        var orders = await db.Orders.AsNoTracking().Include(o => o.Lines)
            .OrderByDescending(o => o.IssuedAt).ToListAsync(ct);
        var suppliers = (await db.Suppliers.AsNoTracking().ToListAsync(ct)).ToDictionary(s => s.Id.Value, s => s.Code);
        return orders.Select(o => ToView(o, suppliers.GetValueOrDefault(o.SupplierId.Value, string.Empty))).ToList();
    }

    private static PurchaseOrderView ToView(PurchaseOrder o, string supplierCode) => new(
        o.Id.Value, o.RequisitionId.Value, o.SupplierId.Value, supplierCode, o.Status.ToString(),
        o.IssuedBySubject, o.IssuedAt,
        o.Lines.Select(l => new OrderLineView(l.ItemCode, l.Quantity, l.Unit)).ToList());
}
