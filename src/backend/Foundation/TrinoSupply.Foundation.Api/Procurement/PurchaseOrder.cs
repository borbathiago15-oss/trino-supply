namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// Pedido de Compra (PO-001 MVP): EMITIDO → RECEBIDO (gera entradas de estoque
/// para itens de catálogo, PO-BR-005) ou CANCELADO. Documentos imutáveis após estado final (PO-BR-004).
/// </summary>
public enum PurchaseOrderStatus : short
{
    Issued = 1,
    Received = 2,
    Cancelled = 3,
}

public class PurchaseOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = string.Empty;   // PO-2026-000001
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Issued;
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;   // snapshot
    public Guid? SourcePrId { get; set; }                      // requisição de origem do processo
    public string? SourcePrNumber { get; set; }
    public Guid? QuotationId { get; set; }                     // processo de cotação de origem (RFQ-001)
    public string? QuotationNumber { get; set; }
    public string? PaymentTerms { get; set; }                  // condições da proposta vencedora
    public int? DeliveryDays { get; set; }
    public decimal? FreightValue { get; set; }
    public Guid? PdfDocumentId { get; set; }                   // último PDF gerado (stored_document)
    public string? Notes { get; set; }
    public decimal TotalValue { get; set; }
    public List<PurchaseOrderItem> Items { get; set; } = [];
    public Guid IssuedBy { get; set; }
    public string IssuedByLabel { get; set; } = string.Empty;
    public Guid? ReceivedBy { get; set; }
    public string? ReceivedByLabel { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public class PurchaseOrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string UnitOfMeasure { get; set; } = "UN";
    public decimal Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public Guid? CatalogItemId { get; set; }   // com vínculo → recebimento gera entrada de estoque
    public string? CatalogCode { get; set; }   // snapshot
    public DateTimeOffset CreatedAt { get; set; }
}
