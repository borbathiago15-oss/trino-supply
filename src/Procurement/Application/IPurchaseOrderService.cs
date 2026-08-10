using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Procurement.Application;

public sealed record OrderLineView(
    string ItemCode, string Description, decimal Quantity, string Unit, decimal UnitPrice,
    decimal IrrfPercent, decimal IssPercent, decimal ServiceValue, decimal IrrfValue, decimal IssValue,
    DateTimeOffset? DeliveryDate);

public sealed record PurchaseOrderView(
    Guid Id, long Number, Guid RequisitionId, Guid PayingCompanyId, string PayingCompanyName,
    Guid SupplierId, string SupplierCode, string SupplierName, string Status, string IssuedBy,
    DateTimeOffset IssuedAt, string PaymentTerms, string PaymentMethod, decimal ProductsValue,
    decimal IpiValue, decimal IcmsValue, decimal DiscountValue, decimal OtherExpenses, string FreightTerms,
    decimal NetValue, IReadOnlyList<OrderLineView> Lines);

/// <summary>Preço do vencedor (concorrência/BID) para uma linha da requisição, casada por código do item.</summary>
public sealed record IssueOrderLineInput(
    string ItemCode, decimal UnitPrice, decimal IrrfPercent = 0m, decimal IssPercent = 0m,
    DateTimeOffset? DeliveryDate = null);

/// <summary>
/// Dados da emissão da OC: empresa pagadora + fornecedor vencedor + preços por linha + totais de cabeçalho.
/// </summary>
public sealed record IssueOrderInput(
    string PayingCompanyCode, string SupplierCode, IReadOnlyList<IssueOrderLineInput> Lines,
    decimal IpiValue = 0m, decimal IcmsValue = 0m, decimal DiscountValue = 0m, decimal OtherExpenses = 0m,
    string? FreightTerms = null);

/// <summary>Pedido de compra / Ordem de Compra (PR-001): emitido de uma requisição aprovada.</summary>
public interface IPurchaseOrderService
{
    /// <summary>Emite a OC selecionando empresa pagadora + fornecedor vencedor + preços (um pedido por requisição).</summary>
    Task<Result<Guid>> IssueFromRequisitionAsync(Guid requisitionId, IssueOrderInput input, CancellationToken ct = default);

    Task<Result<PurchaseOrderView>> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PurchaseOrderView>> ListAsync(CancellationToken ct = default);
}
