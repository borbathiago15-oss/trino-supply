namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// Estados oficiais expostos pela API (PR-001-13 §3.1), derivados da State Machine PR-001-03.
/// SUBMITTED e a validação (ST-002/ST-003) são transientes no MVP: a submissão valida
/// sincronamente e cai em IN_APPROVAL (ST-004) ou RETURNED (ST-007).
/// </summary>
public enum RequisitionStatus : short
{
    Draft = 1,        // ST-001
    Submitted = 2,    // ST-002 (transiente)
    InApproval = 3,   // ST-004 Waiting Approval
    Approved = 4,     // ST-005
    Rejected = 5,     // ST-006
    Returned = 6,     // ST-007 Returned for Adjustment
    Cancelled = 7,    // ST-009 (terminal)
}

public static class RequisitionPriorities
{
    public static readonly string[] All = ["LOW", "NORMAL", "HIGH", "URGENT"];
}

public class PurchaseRequisition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = string.Empty;           // PR-2026-000123 (sequencial — PR-001-11)
    public string Kind { get; set; } = "AVULSA";                 // AVULSA (digitada) | CATALOGO (itens por família — MMS-002)
    public RequisitionStatus Status { get; set; } = RequisitionStatus.Draft;
    public int Cycle { get; set; } = 1;                          // incrementa a cada resubmissão (PR-001-03)
    public string Priority { get; set; } = "NORMAL";
    public DateOnly? NeededBy { get; set; }
    public string Justification { get; set; } = string.Empty;
    public string CostCenter { get; set; } = string.Empty;       // referência textual até FD-001-02/09 existirem
    public string? NeedType { get; set; }                        // Tipo SC (tipo da necessidade)
    public string? DeliveryLocation { get; set; }                // local de entrega
    public string? Company { get; set; }                         // empresa solicitante (grupo)
    public string? InternalNotes { get; set; }                   // observação interna
    public string Currency { get; set; } = "BRL";
    public Guid RequesterId { get; set; }
    public string RequesterLabel { get; set; } = string.Empty;
    public string? DecisionReason { get; set; }                  // motivo de rejeição/devolução/cancelamento
    public Guid? DecidedById { get; set; }
    public string? DecidedByLabel { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public List<RequisitionItem> Items { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public int Version { get; set; } = 1;

    public decimal TotalEstimatedValue =>
        Items.Sum(i => i.Quantity * (i.EstimatedUnitPrice ?? 0));

    public bool IsEditable => Status is RequisitionStatus.Draft or RequisitionStatus.Returned;
    public bool IsTerminal => Status is RequisitionStatus.Cancelled;
}

public class RequisitionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequisitionId { get; set; }
    public Guid? CatalogItemId { get; set; }                     // vínculo com o catálogo (MMS-002), quando houver
    public string? CatalogCode { get; set; }                     // snapshot do código na data da solicitação
    public int Sequence { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string UnitOfMeasure { get; set; } = "UN";
    public decimal? EstimatedUnitPrice { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
