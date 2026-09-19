using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Procurement.Application;

/// <summary>Uma linha conferida na doca (o conferente informa recebido e avariado).</summary>
public sealed record ReceiptLineRequest(
    Guid OrderLineId, decimal QuantityReceived, decimal QuantityDamaged,
    string? Occurrence, string? OccurrenceNote);

/// <summary>Cabeçalho da entrega: nota fiscal + observações + as linhas conferidas.</summary>
public sealed record RegisterReceiptInput(
    string? InvoiceNumber, DateOnly? InvoiceDate, string? Notes, IReadOnlyList<ReceiptLineRequest> Lines);

public sealed record ReceiptLineView(
    Guid OrderLineId, string ItemCode, string Unit, decimal QuantityOrdered, decimal QuantityReceived,
    decimal QuantityDamaged, decimal NetQuantity, string Occurrence, string? OccurrenceNote);

public sealed record ReceiptView(
    Guid Id, Guid PurchaseOrderId, string InvoiceNumber, DateOnly? InvoiceDate, string ReceivedBy,
    DateTimeOffset ReceivedAt, string? Notes, bool StockPosted, IReadOnlyList<ReceiptLineView> Lines);

/// <summary>Linha da OC na visão da conferência: o que foi pedido, o que já entrou e o que falta.</summary>
public sealed record PendingReceiptLineView(
    Guid OrderLineId, string ItemCode, string Description, string Unit,
    decimal QuantityOrdered, decimal QuantityAlreadyReceived, decimal QuantityPending);

/// <summary>Tela de conferência de uma OC: cabeçalho, o que falta receber e o histórico de entregas.</summary>
public sealed record OrderReceiptSummaryView(
    Guid OrderId, long Number, string Status, string SupplierCode, string SupplierName,
    IReadOnlyList<PendingReceiptLineView> Lines, IReadOnlyList<ReceiptView> Receipts);

/// <summary>Quantidade líquida por item que deve entrar no estoque após o recebimento.</summary>
public sealed record StockEntryLine(string ItemCode, decimal Quantity);

/// <summary>Resultado do registro: o recebimento criado e o que precisa ser creditado no Almox.</summary>
public sealed record ReceiptRegistered(
    Guid ReceiptId, IReadOnlyList<StockEntryLine> StockLines, bool OrderComplete, bool HasOccurrence);

/// <summary>
/// Recebimento de mercadoria (MMS-005): conferência física contra a OC. O crédito no estoque é
/// orquestrado pelo host (Materiais é outro bounded context) usando <see cref="ReceiptRegistered.StockLines"/>.
/// </summary>
public interface IGoodsReceiptService
{
    Task<Result<ReceiptRegistered>> RegisterAsync(Guid orderId, RegisterReceiptInput input, CancellationToken ct = default);

    /// <summary>Confirma que a entrada no estoque foi efetivada (idempotente).</summary>
    Task<Result> MarkStockPostedAsync(Guid receiptId, CancellationToken ct = default);

    Task<Result<OrderReceiptSummaryView>> GetOrderReceiptsAsync(Guid orderId, CancellationToken ct = default);
    Task<Result<ReceiptView>> GetAsync(Guid receiptId, CancellationToken ct = default);
}
