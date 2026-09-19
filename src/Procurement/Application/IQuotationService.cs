using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Procurement.Application;

/// <summary>
/// A oferta de um fornecedor para um item, já comparada com as demais: quanto está acima do menor
/// preço da linha e quanto está acima do <b>último preço efetivamente pago</b> pelo item.
/// </summary>
public sealed record QuotationBidView(
    Guid SupplierId, string SupplierCode, string SupplierName,
    decimal UnitPrice, decimal TotalPrice, int? DeliveryDays, string? Notes,
    bool IsLowest, bool IsLate,
    // % acima do menor preço desta linha (0 para o menor).
    decimal PercentAboveLowest,
    // % acima do último preço pago pelo item. Nulo quando não há compra anterior.
    decimal? PercentAboveLastPaid,
    // Sobrepreço: mais de 15% acima do último preço pago (o comprador decide mesmo assim).
    bool OverpriceAlert);

/// <summary>Um item em concorrência com todas as propostas recebidas lado a lado (equalização).</summary>
public sealed record QuotationLineView(
    Guid Id, Guid RequisitionLineId, string ItemCode, decimal Quantity, string Unit,
    Guid? AwardedSupplierId, string? AwardedSupplierCode, decimal? AwardedUnitPrice, string? AwardNote,
    // Último preço unitário pago por este item em OC não cancelada (referência de mercado interna).
    decimal? LastPaidPrice, DateTimeOffset? LastPaidAt,
    IReadOnlyList<QuotationBidView> Bids);

/// <summary>O fornecedor na cotação: se respondeu, em que condições, e como ele entrega (OTIF).</summary>
public sealed record QuotationParticipantView(
    Guid SupplierId, string SupplierCode, string SupplierName,
    bool HasResponded, bool IsLate, DateTimeOffset? RespondedAt,
    string? PaymentTerms, string? FreightTerms, DateOnly? ValidUntil, string? Notes,
    int ItemsQuoted, decimal Total,
    // Faixa de desempenho de entrega (Fase 03) — preço não é o único critério.
    string? OtifTier, decimal? OtifIndex);

public sealed record QuotationView(
    Guid Id, long Number, Guid RequisitionId, string Status, string CreatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset ClosesAt, bool IsClosed, string? Notes,
    string? CancelledBy, DateTimeOffset? CancelledAt, string? CancelReason,
    IReadOnlyList<QuotationLineView> Lines, IReadOnlyList<QuotationParticipantView> Participants);

public sealed record QuotationSummaryView(
    Guid Id, long Number, Guid RequisitionId, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset ClosesAt, bool IsClosed, int LinesCount, int InvitedCount, int RespondedCount,
    int AwardedCount);

/// <summary>Abertura da concorrência: quais linhas pendentes da requisição e quais fornecedores.</summary>
public sealed record OpenQuotationInput(
    IReadOnlyList<Guid> RequisitionLineIds, IReadOnlyList<string> SupplierCodes,
    DateTimeOffset ClosesAt, string? Notes);

public sealed record ProposalBidInput(Guid LineId, decimal UnitPrice, int? DeliveryDays, string? Notes);

/// <summary>Proposta de um fornecedor: condições gerais + preço/prazo dos itens que ele atende.</summary>
public sealed record SubmitProposalInput(
    string SupplierCode, string? PaymentTerms, string? FreightTerms, DateOnly? ValidUntil, string? Notes,
    IReadOnlyList<ProposalBidInput> Bids);

public sealed record AwardLineInput(Guid LineId, string SupplierCode, string? Note);

public sealed record AwardQuotationInput(IReadOnlyList<AwardLineInput> Awards);

/// <summary>OCs geradas a partir da adjudicação — uma por fornecedor vencedor.</summary>
public sealed record QuotationOrdersResult(IReadOnlyList<Guid> OrderIds);

/// <summary>
/// Cotação / concorrência (RFQ — Fase 03): leva os itens pendentes de uma requisição aprovada a
/// vários fornecedores, monta o <b>mapa de equalização</b> (preço × prazo × OTIF, lado a lado) e
/// registra a adjudicação por item. O fechamento do ciclo é a emissão das OCs — uma por fornecedor
/// vencedor, com o preço congelado na adjudicação e o prazo prometido virando a data de entrega da
/// OC, que é justamente o que o scorecard OTIF vai medir no recebimento.
/// </summary>
public interface IQuotationService
{
    Task<Result<Guid>> OpenAsync(Guid requisitionId, OpenQuotationInput input, CancellationToken ct = default);
    Task<Result> SubmitProposalAsync(Guid quotationId, SubmitProposalInput input, CancellationToken ct = default);
    Task<Result> AwardAsync(Guid quotationId, AwardQuotationInput input, CancellationToken ct = default);
    Task<Result> CancelAsync(Guid quotationId, string reason, CancellationToken ct = default);

    /// <summary>Emite as OCs dos itens adjudicados, agrupando por fornecedor vencedor.</summary>
    Task<Result<QuotationOrdersResult>> IssueOrdersAsync(Guid quotationId, CancellationToken ct = default);

    /// <summary>Mapa de equalização completo da cotação.</summary>
    Task<Result<QuotationView>> GetAsync(Guid quotationId, CancellationToken ct = default);
    Task<IReadOnlyList<QuotationSummaryView>> ListAsync(int limit = 200, CancellationToken ct = default);
}
