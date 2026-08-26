namespace TrinoSupply.Foundation.Api.Materials;

/// <summary>
/// Solicitação de material ao almoxarifado (MMS-003 MVP): itens sempre do catálogo;
/// o almoxarifado atende com baixa de estoque; item sem saldo segue rota de compra (rota mista).
/// </summary>
public enum MaterialRequisitionStatus : short
{
    Submitted = 1,          // aguardando a aprovação do responsável do centro (Nível 1)
    Fulfilled = 2,          // atendida integralmente pelo almoxarifado
    PartiallyFulfilled = 3, // atendida em parte; o que faltou virou solicitação de compra
    PurchaseRoute = 4,      // nada havia em estoque — tudo virou solicitação de compra
    Cancelled = 5,
    Approved = 6,           // aprovada pelo Nível 1 — na fila de atendimento do estoque
    Rejected = 7,           // recusada na aprovação do centro
}

public enum MaterialItemStatus : short
{
    Pending = 1,
    Fulfilled = 2,          // entregue pelo almoxarifado
    PurchaseRoute = 3,      // não havia no estoque — virou solicitação de compra
    PartiallyFulfilled = 4, // entregue em parte; o restante virou compra
}

public class MaterialRequisition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = string.Empty;   // MR-2026-000001
    public MaterialRequisitionStatus Status { get; set; } = MaterialRequisitionStatus.Submitted;
    public string CostCenter { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public Guid RequesterId { get; set; }
    public string RequesterLabel { get; set; } = string.Empty;
    public List<MaterialRequisitionItem> Items { get; set; } = [];
    public Guid? FulfilledBy { get; set; }
    public string? FulfilledByLabel { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
    // aprovação do responsável do centro (Nível 1) antes de o estoque atender
    public Guid? ApprovedById { get; set; }
    public string? ApprovedByLabel { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? DecisionReason { get; set; }
    // solicitação de compra gerada com o que faltou no atendimento
    public Guid? PurchaseRequisitionId { get; set; }
    public string? PurchaseRequisitionNumber { get; set; }
    // Triagem de demandas (tickets): responsável designado pelo atendimento
    public Guid? AssignedToId { get; set; }
    public string? AssignedToLabel { get; set; }
    public Guid? AssignedById { get; set; }
    public string? AssignedByLabel { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public class MaterialRequisitionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequisitionId { get; set; }
    public Guid CatalogItemId { get; set; }
    public string CatalogCode { get; set; } = string.Empty;   // snapshot
    public string Description { get; set; } = string.Empty;   // snapshot
    public string UnitOfMeasure { get; set; } = "UN";
    public decimal Quantity { get; set; }                     // pedida pelo solicitante (não muda)
    public decimal? ApprovedQuantity { get; set; }            // liberada pelo Nível 1 (≤ pedida)
    public decimal FulfilledQuantity { get; set; }            // entregue pelo almoxarifado
    public MaterialItemStatus Status { get; set; } = MaterialItemStatus.Pending;
    public Guid? StockMovementId { get; set; }                // saída vinculada (acervo anterior)
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>O que o Nível 1 liberou — antes da aprovação, o que foi pedido.</summary>
    public decimal EffectiveQuantity => ApprovedQuantity ?? Quantity;
}
