namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>Situação única do pedido, como aparece para o solicitante e para compras.</summary>
public record ProcessStatusView(string Key, string Label, string Tone, string Explanation);

/// <summary>
/// Os oito status do fluxo de compras da revisão de telas (2026-08-26). A solicitação, a
/// cotação e a ordem de compra são etapas de um processo só; aqui elas viram uma única
/// situação, para o solicitante não precisar saber em qual tabela o pedido está parado.
/// </summary>
public static class ProcessStatus
{
    public static readonly ProcessStatusView Draft =
        new("RASCUNHO", "Rascunho", "dev", "Solicitação ainda não enviada.");
    public static readonly ProcessStatusView Pending =
        new("PENDENTE", "Pendente", "", "Pedido aguardando ação do comprador.");
    public static readonly ProcessStatusView InQuotation =
        new("EM_COTACAO", "Em Cotação", "warn", "Comprador iniciou as cotações.");
    public static readonly ProcessStatusView BudgetPresented =
        new("ORCAMENTO_APRESENTADO", "Orçamento apresentado", "info",
            "O comprador fechou o orçamento. Só vai à aprovação se alguém decidir comprar.");
    public static readonly ProcessStatusView AwaitingApproval =
        new("AGUARDANDO_APROVACAO", "Aguardando Aprovação", "teal", "Comprador enviou as cotações para os aprovadores.");
    public static readonly ProcessStatusView Approved =
        new("PEDIDO_APROVADO", "Pedido Aprovado", "on", "Pedido aprovado na alçada.");
    public static readonly ProcessStatusView Rejected =
        new("PEDIDO_REJEITADO", "Pedido Rejeitado", "off", "Aprovador rejeitou o pedido.");
    public static readonly ProcessStatusView PoInvoicing =
        new("OC_FATURAMENTO", "OC/Faturamento", "info", "OC emitida — aguardando faturamento e entrega.");
    public static readonly ProcessStatusView Delivered =
        new("PEDIDO_ENTREGUE", "Pedido Entregue", "purple", "O pedido foi entregue.");
    public static readonly ProcessStatusView CancelledPartial =
        new("CANCELADO_PARCIAL", "Pedido Cancelado/Parcial", "orange", "O pedido foi cancelado ou entregue em parte.");
    public static readonly ProcessStatusView Returned =
        new("DEVOLVIDO", "Devolvido para ajuste", "warn", "O aprovador devolveu a solicitação ao solicitante.");

    /// <summary>Resolve a situação a partir da solicitação e do que já existe de cotação e OC.</summary>
    public static ProcessStatusView Of(PurchaseRequisition pr, Quotation? quotation, PurchaseOrder? order)
    {
        if (order is not null)
        {
            return order.Status switch
            {
                PurchaseOrderStatus.Received => Delivered,
                PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.PartiallyReceived => CancelledPartial,
                _ => PoInvoicing,
            };
        }

        if (quotation is not null)
        {
            return quotation.Status switch
            {
                QuotationStatus.Open or QuotationStatus.Analysis => InQuotation,
                QuotationStatus.BudgetPresented => BudgetPresented,
                QuotationStatus.AwaitingManager or QuotationStatus.AwaitingDirector => AwaitingApproval,
                QuotationStatus.ApprovedForIssue or QuotationStatus.PoIssued => Approved,
                QuotationStatus.Rejected => Rejected,
                _ => CancelledPartial,
            };
        }

        return pr.Status switch
        {
            RequisitionStatus.Draft => Draft,
            RequisitionStatus.Returned => Returned,
            RequisitionStatus.Rejected => Rejected,
            RequisitionStatus.Cancelled => CancelledPartial,
            RequisitionStatus.InApproval => AwaitingApproval,   // acervo do fluxo anterior
            _ => Pending,
        };
    }
}
