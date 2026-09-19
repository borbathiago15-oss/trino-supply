using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Procurement.Application;

/// <summary>Uma divergência apontada pela conciliação, pronta para virar linha de relatório.</summary>
public sealed record MatchDivergenceView(
    string ItemCode, string Kind, string Code, decimal Expected, decimal Found,
    decimal DeviationPercent, bool WithinTolerance, string Message);

/// <summary>
/// Um item nas quatro colunas da tela de conciliação: pedido (OC), faturado (NF-e), físico (doca)
/// e o veredito.
/// </summary>
public sealed record MatchLineView(
    string ItemCode,
    decimal QuantityOrdered, decimal UnitPriceOrdered,
    decimal? QuantityInvoiced, decimal? UnitPriceInvoiced,
    decimal QuantityReceived, decimal QuantityDamaged,
    bool Matched, bool NotInvoiced, IReadOnlyList<MatchDivergenceView> Divergences);

public sealed record InvoiceLineView(
    int ItemNumber, string ProductCode, string Description, string? Ncm, string? Cfop,
    string Unit, decimal Quantity, decimal UnitPrice, decimal TotalValue);

public sealed record PurchaseInvoiceView(
    Guid Id, Guid PurchaseOrderId, string AccessKey, long Number, string Series,
    DateTimeOffset IssuedAt, string EmitterTaxId, string EmitterName, decimal TotalValue,
    string ImportedBy, DateTimeOffset ImportedAt, string Status, DateTimeOffset? MatchedAt,
    string? MatchSummary, bool ReleasedToFinance, string? ReleasedBy, DateTimeOffset? ReleasedAt,
    string? ReleaseNote, IReadOnlyList<InvoiceLineView> Lines, IReadOnlyList<MatchLineView> Match);

/// <summary>Tela de conciliação de uma OC: as notas importadas e a conferência das três pontas.</summary>
public sealed record OrderMatchView(
    Guid OrderId, long Number, string SupplierCode, string SupplierName, string Status,
    decimal PriceTolerancePercent, decimal QuantityTolerancePercent,
    IReadOnlyList<PurchaseInvoiceView> Invoices);

/// <summary>Resultado da ingestão: a nota criada e o veredito imediato da conciliação.</summary>
public sealed record InvoiceImported(Guid InvoiceId, string AccessKey, string Status, bool ReleasedToFinance,
    IReadOnlyList<MatchDivergenceView> Divergences);

/// <summary>
/// Conciliação fiscal de três pontas (Fase 05): a NF-e entra pelo XML, é cruzada com a OC e com a
/// conferência da doca, e só segue para o financeiro se as três pontas fecharem dentro da
/// tolerância. Divergência trava — e a liberação da exceção exige justificativa registrada.
/// </summary>
public interface IInvoiceMatchService
{
    /// <summary>Importa o XML da NF-e para uma OC e roda a conciliação na hora.</summary>
    Task<Result<InvoiceImported>> ImportAsync(Guid orderId, string xml, CancellationToken ct = default);

    /// <summary>Reprocessa a conciliação de uma nota (após corrigir o recebimento, por exemplo).</summary>
    Task<Result<InvoiceImported>> RematchAsync(Guid invoiceId, CancellationToken ct = default);

    /// <summary>Libera para o financeiro uma nota divergente, com justificativa obrigatória.</summary>
    Task<Result> ReleaseAsync(Guid invoiceId, string? note, CancellationToken ct = default);

    Task<Result<OrderMatchView>> GetByOrderAsync(Guid orderId, CancellationToken ct = default);
}
