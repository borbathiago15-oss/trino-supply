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

/// <summary>Evento: uma OC foi emitida para um fornecedor (alimenta a projeção de histórico do fornecedor).</summary>
public sealed record OrderIssued(
    Guid EventId, DateTimeOffset OccurredAt, Guid CompanyId, Guid OrderId, long Number,
    Guid SupplierId, decimal NetValue) : IDomainEvent;

/// <summary>Evento: uma OC emitida foi cancelada (reverte a projeção do fornecedor).</summary>
public sealed record OrderCancelled(
    Guid EventId, DateTimeOffset OccurredAt, Guid CompanyId, Guid OrderId,
    Guid SupplierId, decimal NetValue) : IDomainEvent;

/// <summary>
/// Linha do pedido (snapshot da linha da requisição + preço do vencedor da concorrência/BID).
/// Guarda o valor unitário e os percentuais fiscais (IRRF/ISS) para compor a OC. Escopada ao tenant (RLS).
/// </summary>
public sealed class OrderLine : IBelongsToTenant
{
    private OrderLine(
        Guid id, CompanyId companyId, PurchaseOrderId orderId, string itemCode, string description,
        decimal quantity, string unit, decimal unitPrice, decimal irrfPercent, decimal issPercent,
        DateTimeOffset? deliveryDate)
    {
        Id = id;
        CompanyId = companyId;
        OrderId = orderId;
        ItemCode = itemCode;
        Description = description;
        Quantity = quantity;
        Unit = unit;
        UnitPrice = unitPrice;
        IrrfPercent = irrfPercent;
        IssPercent = issPercent;
        DeliveryDate = deliveryDate;
    }

    private OrderLine() { } // EF

    public Guid Id { get; private set; }
    public CompanyId CompanyId { get; private set; }
    public PurchaseOrderId OrderId { get; private set; }
    public string ItemCode { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public decimal IrrfPercent { get; private set; }
    public decimal IssPercent { get; private set; }
    public DateTimeOffset? DeliveryDate { get; private set; }

    /// <summary>Valor do serviço/produto da linha (Vlr.Serviço = quantidade × valor unitário).</summary>
    public decimal ServiceValue => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);
    public decimal IrrfValue => Math.Round(ServiceValue * IrrfPercent / 100m, 2, MidpointRounding.AwayFromZero);
    public decimal IssValue => Math.Round(ServiceValue * IssPercent / 100m, 2, MidpointRounding.AwayFromZero);

    public static OrderLine Create(
        CompanyId companyId, PurchaseOrderId orderId, string itemCode, string description, decimal quantity,
        string unit, decimal unitPrice, decimal irrfPercent, decimal issPercent, DateTimeOffset? deliveryDate) =>
        new(Guid.NewGuid(), companyId, orderId, itemCode, description ?? string.Empty, quantity, unit,
            unitPrice, irrfPercent, issPercent, deliveryDate);
}

/// <summary>Dados de uma linha na emissão do pedido (preço do vencedor da concorrência).</summary>
public sealed record OrderLineInput(
    string ItemCode, string Description, decimal Quantity, string Unit, decimal UnitPrice,
    decimal IrrfPercent, decimal IssPercent, DateTimeOffset? DeliveryDate);

/// <summary>Totais e condições de cabeçalho da OC informados na emissão.</summary>
public sealed record OrderTotalsInput(
    decimal IpiValue, decimal IcmsValue, decimal DiscountValue, decimal OtherExpenses, string? FreightTerms);

/// <summary>
/// Pedido de compra / Ordem de Compra (PR-001): emitido a partir de uma requisição APROVADA. Na emissão
/// selecionam-se a <b>empresa pagadora</b> (CNPJ responsável) e o <b>fornecedor vencedor</b> da
/// concorrência/BID, com os preços por linha. Recebe um número sequencial por tenant. Um pedido por
/// requisição (índice único). Fecha o procure-to-pay e é a fonte do PDF da OC.
/// </summary>
public sealed class PurchaseOrder : AggregateRoot<PurchaseOrderId>, IBelongsToTenant
{
    private readonly List<OrderLine> _lines = new();

    private PurchaseOrder(
        PurchaseOrderId id, CompanyId companyId, long number, RequisitionId requisitionId,
        PayingCompanyId payingCompanyId, SupplierId supplierId, string paymentTerms, string paymentMethod,
        decimal ipiValue, decimal icmsValue, decimal discountValue, decimal otherExpenses, string freightTerms,
        string issuedBySubject, DateTimeOffset issuedAt) : base(id)
    {
        CompanyId = companyId;
        Number = number;
        RequisitionId = requisitionId;
        PayingCompanyId = payingCompanyId;
        SupplierId = supplierId;
        PaymentTerms = paymentTerms;
        PaymentMethod = paymentMethod;
        IpiValue = ipiValue;
        IcmsValue = icmsValue;
        DiscountValue = discountValue;
        OtherExpenses = otherExpenses;
        FreightTerms = freightTerms;
        IssuedBySubject = issuedBySubject;
        IssuedAt = issuedAt;
        Status = PurchaseOrderStatus.Issued;
    }

    private PurchaseOrder() : base(default!) { } // EF

    public CompanyId CompanyId { get; private set; }
    public long Number { get; private set; }                            // nº da OC (sequencial por tenant)
    public RequisitionId RequisitionId { get; private set; }
    public PayingCompanyId PayingCompanyId { get; private set; }        // empresa pagadora (comprador da OC)
    public SupplierId SupplierId { get; private set; }                  // fornecedor vencedor
    public string PaymentTerms { get; private set; } = string.Empty;    // snapshot Cond.Pgto
    public string PaymentMethod { get; private set; } = string.Empty;   // snapshot Forma Pgto
    public decimal IpiValue { get; private set; }
    public decimal IcmsValue { get; private set; }
    public decimal DiscountValue { get; private set; }
    public decimal OtherExpenses { get; private set; }
    public string FreightTerms { get; private set; } = string.Empty;
    public string IssuedBySubject { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public string? CancelledBySubject { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancelReason { get; private set; }
    public IReadOnlyList<OrderLine> Lines => _lines;

    /// <summary>Valor dos produtos (soma dos valores de serviço das linhas).</summary>
    public decimal ProductsValue => _lines.Sum(l => l.ServiceValue);
    /// <summary>Valor líquido = produtos + IPI − descontos + outras despesas.</summary>
    public decimal NetValue => ProductsValue + IpiValue - DiscountValue + OtherExpenses;

    /// <summary>Emite o pedido com número sequencial, empresa pagadora, fornecedor vencedor e linhas/preços.</summary>
    public static Result<PurchaseOrder> Issue(
        CompanyId companyId, long number, RequisitionId requisitionId, PayingCompanyId payingCompanyId,
        SupplierId supplierId, string paymentTerms, string paymentMethod, OrderTotalsInput totals,
        string issuedBySubject, DateTimeOffset issuedAt, IEnumerable<OrderLineInput> lines)
    {
        if (string.IsNullOrWhiteSpace(issuedBySubject))
            return Result.Failure<PurchaseOrder>(new Error("purchases.order.issuer_required", "Emissor obrigatório."));
        if (number <= 0)
            return Result.Failure<PurchaseOrder>(new Error("purchases.order.number_required", "Número da OC inválido."));

        var materialized = lines.ToList();
        if (materialized.Count == 0)
            return Result.Failure<PurchaseOrder>(new Error("purchases.order.no_lines", "Requisição sem linhas para o pedido."));
        if (materialized.Any(l => l.Quantity <= 0))
            return Result.Failure<PurchaseOrder>(new Error("purchases.order.qty_invalid", "Quantidade das linhas deve ser positiva."));
        if (materialized.Any(l => l.UnitPrice < 0))
            return Result.Failure<PurchaseOrder>(new Error("purchases.order.price_invalid", "Valor unitário não pode ser negativo."));

        var order = new PurchaseOrder(
            PurchaseOrderId.New(), companyId, number, requisitionId, payingCompanyId, supplierId,
            (paymentTerms ?? string.Empty).Trim(), (paymentMethod ?? string.Empty).Trim(),
            totals.IpiValue, totals.IcmsValue, totals.DiscountValue, totals.OtherExpenses,
            (totals.FreightTerms ?? "C - POR CONTA DO DESTINATÁRIO").Trim(), issuedBySubject, issuedAt);

        foreach (var l in materialized)
            order._lines.Add(OrderLine.Create(
                companyId, order.Id, l.ItemCode, l.Description, l.Quantity, l.Unit, l.UnitPrice,
                l.IrrfPercent, l.IssPercent, l.DeliveryDate));

        order.Raise(new OrderIssued(
            Guid.NewGuid(), issuedAt, companyId.Value, order.Id.Value, number, supplierId.Value, order.NetValue));
        return Result.Success(order);
    }

    /// <summary>
    /// Cancela a OC (só uma OC <b>emitida</b> pode ser cancelada). Registra motivo/quem/quando para a
    /// trilha. Após cancelar, a requisição volta a poder gerar uma nova OC (índice único parcial só
    /// conta pedidos emitidos).
    /// </summary>
    public Result Cancel(string cancelledBySubject, string reason, DateTimeOffset now)
    {
        if (Status != PurchaseOrderStatus.Issued)
            return Result.Failure(new Error("purchases.order.not_issued", "Só uma OC emitida pode ser cancelada."));
        if (string.IsNullOrWhiteSpace(cancelledBySubject))
            return Result.Failure(new Error("purchases.order.canceller_required", "Responsável pelo cancelamento é obrigatório."));
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("purchases.order.cancel_reason_required", "Informe o motivo do cancelamento."));

        Status = PurchaseOrderStatus.Cancelled;
        CancelledBySubject = cancelledBySubject;
        CancelledAt = now;
        CancelReason = reason.Trim();
        Version++;
        Raise(new OrderCancelled(Guid.NewGuid(), now, CompanyId.Value, Id.Value, SupplierId.Value, NetValue));
        return Result.Success();
    }
}
