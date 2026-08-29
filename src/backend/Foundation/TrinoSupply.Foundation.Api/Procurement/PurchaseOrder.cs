namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// Pedido de Compra (PO-001 MVP): EMITIDO → RECEBIDO (gera entradas de estoque
/// para itens de catálogo, PO-BR-005) ou CANCELADO. Documentos imutáveis após estado final (PO-BR-004).
/// </summary>
public enum PurchaseOrderStatus : short
{
    Issued = 1,             // OC emitida / registrada no ERP — aguardando faturamento
    Received = 2,           // entrega concluída
    Cancelled = 3,          // pedido cancelado (motivo obrigatório)
    Invoiced = 4,           // fornecedor faturou (NF registrada) — aguardando entrega
    PartiallyReceived = 5,  // entrega parcial: parte chegou, o resto segue pendente
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
    // OC feita no ERP: o sistema só amarra a solicitação ao documento oficial (revisão de telas)
    public string? ErpNumber { get; set; }                      // número da OC no ERP
    public DateOnly? ErpIssuedOn { get; set; }
    /// <summary>Data prometida: data da O.C. + prazo de entrega da proposta vencedora (OTIF).</summary>
    public DateOnly? PromisedDate { get; set; }                  // data da OC (base do lead time)
    public Guid? ErpDocumentId { get; set; }                    // anexo da OC
    public string? ErpFileName { get; set; }
    public DateTimeOffset? DeliveryCompletedAt { get; set; }    // data da conclusão da entrega
    public string? Notes { get; set; }
    public decimal TotalValue { get; set; }
    public List<PurchaseOrderItem> Items { get; set; } = [];
    public Guid IssuedBy { get; set; }
    public string IssuedByLabel { get; set; } = string.Empty;
    public Guid? ReceivedBy { get; set; }
    public string? ReceivedByLabel { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public string? CancelReason { get; set; }
    public List<PurchaseOrderInvoice> Invoices { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;

    /// <summary>Ainda falta material chegar (usado na entrega parcial).</summary>
    public bool HasPendingDelivery => Items.Any(i => i.ReceivedQuantity < i.Quantity);

    // ---- OTIF (derivado; nada é persistido além da data prometida) -----------
    /// <summary>Entrega no prazo: encerrada até a data prometida. Null enquanto não encerrar (ou sem data).</summary>
    public bool? OnTime => DeliveryCompletedAt is null || PromisedDate is null
        ? null
        : DateOnly.FromDateTime(DeliveryCompletedAt.Value.UtcDateTime) <= PromisedDate;

    /// <summary>Entrega completa: tudo o que foi pedido chegou (saldo encerrado sem chegar = false).</summary>
    public bool? InFull => DeliveryCompletedAt is null ? null : !HasPendingDelivery;

    /// <summary>OTIF do pedido = no prazo E completo.</summary>
    public bool? Otif => OnTime is null || InFull is null ? null : OnTime.Value && InFull.Value;
}

/// <summary>Nota fiscal do faturamento — uma OC pode ter mais de uma (revisão de telas 2026-08-26).</summary>
public class PurchaseOrderInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateOnly IssuedOn { get; set; }
    public decimal? Value { get; set; }
    public Guid? DocumentId { get; set; }
    public string? FileName { get; set; }
    public Guid CreatedBy { get; set; }
    public string CreatedByLabel { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
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
    public decimal ReceivedQuantity { get; set; }   // acumulado das entregas (parciais ou total)
    public DateTimeOffset CreatedAt { get; set; }
}
