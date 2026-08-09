using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Procurement.Domain;

public readonly record struct PurchaseOrderId(Guid Value)
{
    public static PurchaseOrderId New() => new(Guid.NewGuid());
    public static PurchaseOrderId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("PurchaseOrderId não pode ser vazio.", nameof(value))
        : new PurchaseOrderId(value);
    public override string ToString() => Value.ToString();
}

public enum PurchaseOrderStatus
{
    Issued = 1,
    Cancelled = 2
}

/// <summary>Linha do pedido (snapshot da linha da requisição). Escopada ao tenant (RLS).</summary>
public sealed class OrderLine : IBelongsToTenant
{
    private OrderLine(Guid id, CompanyId companyId, PurchaseOrderId orderId, string itemCode, decimal quantity, string unit)
    {
        Id = id;
        CompanyId = companyId;
        OrderId = orderId;
        ItemCode = itemCode;
        Quantity = quantity;
        Unit = unit;
    }

    private OrderLine() { } // EF

    public Guid Id { get; private set; }
    public CompanyId CompanyId { get; private set; }
    public PurchaseOrderId OrderId { get; private set; }
    public string ItemCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string Unit { get; private set; } = string.Empty;

    public static OrderLine Create(CompanyId companyId, PurchaseOrderId orderId, string itemCode, decimal quantity, string unit) =>
        new(Guid.NewGuid(), companyId, orderId, itemCode, quantity, unit);
}

/// <summary>
/// Pedido de compra (PR-001): emitido a partir de uma requisição APROVADA, para um fornecedor,
/// copiando as linhas. Um pedido por requisição (garantido por índice único). Fecha o procure-to-pay.
/// </summary>
public sealed class PurchaseOrder : AggregateRoot<PurchaseOrderId>, IBelongsToTenant
{
    private readonly List<OrderLine> _lines = new();

    private PurchaseOrder(
        PurchaseOrderId id, CompanyId companyId, RequisitionId requisitionId, SupplierId supplierId,
        string issuedBySubject, DateTimeOffset issuedAt) : base(id)
    {
        CompanyId = companyId;
        RequisitionId = requisitionId;
        SupplierId = supplierId;
        IssuedBySubject = issuedBySubject;
        IssuedAt = issuedAt;
        Status = PurchaseOrderStatus.Issued;
    }

    private PurchaseOrder() : base(default!) { } // EF

    public CompanyId CompanyId { get; private set; }
    public RequisitionId RequisitionId { get; private set; }
    public SupplierId SupplierId { get; private set; }
    public string IssuedBySubject { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public IReadOnlyList<OrderLine> Lines => _lines;

    /// <summary>Emite o pedido copiando as linhas fornecidas (snapshot da requisição aprovada).</summary>
    public static Result<PurchaseOrder> Issue(
        CompanyId companyId, RequisitionId requisitionId, SupplierId supplierId, string issuedBySubject,
        DateTimeOffset issuedAt, IEnumerable<(string ItemCode, decimal Quantity, string Unit)> lines)
    {
        if (string.IsNullOrWhiteSpace(issuedBySubject))
            return Result.Failure<PurchaseOrder>(new Error("purchases.order.issuer_required", "Emissor obrigatório."));

        var materialized = lines.ToList();
        if (materialized.Count == 0)
            return Result.Failure<PurchaseOrder>(new Error("purchases.order.no_lines", "Requisição sem linhas para o pedido."));

        var order = new PurchaseOrder(PurchaseOrderId.New(), companyId, requisitionId, supplierId, issuedBySubject, issuedAt);
        foreach (var l in materialized)
            order._lines.Add(OrderLine.Create(companyId, order.Id, l.ItemCode, l.Quantity, l.Unit));
        return Result.Success(order);
    }
}
