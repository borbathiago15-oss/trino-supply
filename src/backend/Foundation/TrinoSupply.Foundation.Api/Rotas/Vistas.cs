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
    public static object PoView(PurchaseOrder o) => new
    {
        id = o.Id, number = o.Number,
        status = o.Status switch
        {
            PurchaseOrderStatus.Issued => "EMITIDO",
            PurchaseOrderStatus.Invoiced => "FATURADO",
            PurchaseOrderStatus.PartiallyReceived => "PARCIAL",
            PurchaseOrderStatus.Received => "RECEBIDO",
            _ => "CANCELADO",
        },
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
