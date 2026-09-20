using TrinoSupply.Foundation.Api.Auth;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// Vistas de resposta que mais de um módulo publica.
///
/// `PoView` mora aqui porque o pedido de compra aparece nos dois lados: na
/// própria tela de pedidos e no fim do processo de cotação, quando a O.C. é
/// registrada. Vista que só um módulo usa fica no arquivo dele (ARQ-A).
/// </summary>
public static class Vistas
{
    /// <param name="inactiveCatalogCodes">
    /// Códigos do pedido que estão inativos no catálogo — o recebimento deles é recusado
    /// (IV-ERR-010) e a tela precisa saber disso antes do formulário. <c>null</c> quer dizer
    /// "não conferido aqui", e não "nenhum": só a leitura do pedido em si faz essa consulta.
    /// </param>
    public static string PoStatusLabel(PurchaseOrderStatus s) => s switch
    {
        PurchaseOrderStatus.Issued => "EMITIDO",
        PurchaseOrderStatus.Invoiced => "FATURADO",
        PurchaseOrderStatus.PartiallyReceived => "PARCIAL",
        PurchaseOrderStatus.Received => "RECEBIDO",
        _ => "CANCELADO",
    };

    public static object PoView(PurchaseOrder o, IReadOnlyList<string>? inactiveCatalogCodes = null) => new
    {
        inactiveCatalogCodes,
        id = o.Id, number = o.Number,
        status = PoStatusLabel(o.Status),
        supplierId = o.SupplierId, supplierName = o.SupplierName,
        sourcePrNumber = o.SourcePrNumber, quotationNumber = o.QuotationNumber,
        paymentTerms = o.PaymentTerms, deliveryDays = o.DeliveryDays, freightValue = o.FreightValue,
        families = o.Families,
        notes = o.Notes, totalValue = o.TotalValue,
        issuedByLabel = o.IssuedByLabel, receivedByLabel = o.ReceivedByLabel, receivedAt = o.ReceivedAt,
        cancelReason = o.CancelReason, createdAt = o.CreatedAt,
        erpNumber = o.ErpNumber, erpIssuedOn = o.ErpIssuedOn,
        // fechado sem O.C. do ERP: a observação que autorizou a exceção (PO-BR-011)
        noErpReason = o.NoErpReason,
        promisedDate = o.PromisedDate, onTime = o.OnTime, inFull = o.InFull, otif = o.Otif,
        referenceSavingTotal = o.Items.Any(i => i.ReferenceSaving != null)
            ? o.Items.Sum(i => i.ReferenceSaving ?? 0) : (decimal?)null,
        erpDocumentId = o.ErpDocumentId, erpFileName = o.ErpFileName,
        deliveryCompletedAt = o.DeliveryCompletedAt,
        pendingDelivery = o.HasPendingDelivery,
        // ainda há quantidade sem O.C. do ERP e sem a observação da exceção
        erpPending = o.HasErpPending,
        erpDocuments = o.ErpDocuments.OrderBy(d => d.CreatedAt).Select(d => new
        {
            id = d.Id, number = d.Number, issuedOn = d.IssuedOn, documentId = d.DocumentId, fileName = d.FileName,
            notes = d.Notes, createdByLabel = d.CreatedByLabel, createdAt = d.CreatedAt,
            items = d.Items.Select(i => new { itemId = i.OrderItemId, quantity = i.Quantity }),
        }),
        invoices = o.Invoices.OrderBy(i => i.IssuedOn).Select(i => new
        {
            id = i.Id, number = i.Number, issuedOn = i.IssuedOn, value = i.Value,
            documentId = i.DocumentId, fileName = i.FileName,
            createdByLabel = i.CreatedByLabel, createdAt = i.CreatedAt,
        }),
        items = o.Items.Select(i => new
        {
            itemId = i.Id, description = i.Description, unitOfMeasure = i.UnitOfMeasure, quantity = i.Quantity,
            receivedQuantity = i.ReceivedQuantity, pendingQuantity = i.Quantity - i.ReceivedQuantity,
            erpCoveredQuantity = o.ErpCovered(i.Id), erpPendingQuantity = i.Quantity - o.ErpCovered(i.Id),
            rejectedQuantity = i.RejectedQuantity, rejectionReason = i.RejectionReason,
            lastPaidUnitPrice = i.LastPaidUnitPrice, referenceSaving = i.ReferenceSaving,
            sourcePrNumber = i.SourcePrNumber ?? o.SourcePrNumber,
            unitPrice = i.UnitPrice, catalogCode = i.CatalogCode, catalogItemId = i.CatalogItemId,
            family = i.Family,
        }),
    };

    /// <summary>A resposta do login: os tokens e quem entrou.</summary>
    public static object ToResponse(AuthTokens t) => new
    {
        accessToken = t.AccessToken,
        tokenType = "Bearer",
        expiresIn = t.ExpiresInSeconds,
        refreshToken = t.RefreshToken,
        user = new
        {
            id = t.User.Id, email = t.User.Email, name = t.User.Name, role = t.User.Role,
            modules = AppModules.EffectiveFor(t.User),
            // a tela usa isto para levar direto à troca de senha no primeiro acesso
            mustChangePassword = t.User.MustChangePassword,
        },
    };
}
