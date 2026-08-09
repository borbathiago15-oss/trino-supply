using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Procurement.Application;

public sealed record OrderLineView(string ItemCode, decimal Quantity, string Unit);
public sealed record PurchaseOrderView(
    Guid Id, Guid RequisitionId, Guid SupplierId, string SupplierCode, string Status,
    string IssuedBy, DateTimeOffset IssuedAt, IReadOnlyList<OrderLineView> Lines);

/// <summary>Pedido de compra (PR-001): emitido de uma requisição aprovada para um fornecedor.</summary>
public interface IPurchaseOrderService
{
    /// <summary>Emite um pedido a partir de uma requisição APROVADA (um pedido por requisição).</summary>
    Task<Result<Guid>> IssueFromRequisitionAsync(Guid requisitionId, string supplierCode, CancellationToken ct = default);

    Task<Result<PurchaseOrderView>> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PurchaseOrderView>> ListAsync(CancellationToken ct = default);
}
