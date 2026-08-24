namespace TrinoSupply.Foundation.Api.Materials;

/// <summary>
/// Solicitação de material ao almoxarifado (MMS-003 MVP): itens sempre do catálogo;
/// o almoxarifado atende com baixa de estoque; item sem saldo segue rota de compra (rota mista).
/// </summary>
public enum MaterialRequisitionStatus : short
{
    Submitted = 1,        // aguardando o almoxarifado
    Fulfilled = 2,        // todos os itens entregues
    PartiallyFulfilled = 3, // entregue o que havia; restante em rota de compra
    PurchaseRoute = 4,    // nenhum item tinha saldo — tudo para compras
    Cancelled = 5,
}

public enum MaterialItemStatus : short
{
    Pending = 1,
    Fulfilled = 2,        // entregue (saída de estoque vinculada)
    PurchaseRoute = 3,    // sem saldo — demanda para PR-001
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
    public decimal Quantity { get; set; }
    public MaterialItemStatus Status { get; set; } = MaterialItemStatus.Pending;
    public Guid? StockMovementId { get; set; }                // saída vinculada quando entregue
    public DateTimeOffset CreatedAt { get; set; }
}
