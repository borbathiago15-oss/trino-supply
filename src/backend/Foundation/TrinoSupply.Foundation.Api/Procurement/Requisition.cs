namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// Estados oficiais expostos pela API (PR-001-13 §3.1), derivados da State Machine PR-001-03.
/// Fluxo atual: a SC enviada (SUBMITTED) vai direto para Suprimentos cotar; a autorização
/// acontece uma vez só no processo de cotação, com os preços do mapa de propostas
/// (Gerente do CC → Diretor). APPROVED = compra autorizada pela alçada.
/// IN_APPROVAL só existe nas solicitações anteriores a essa mudança.
/// </summary>
public enum RequisitionStatus : short
{
    Draft = 1,        // ST-001
    Submitted = 2,    // enviada — em cotação com Suprimentos
    InApproval = 3,   // legado: autorização prévia sem preço
    Approved = 4,     // compra autorizada na alçada (com preços)
    Rejected = 5,     // ST-006
    Returned = 6,     // ST-007 Returned for Adjustment
    Cancelled = 7,    // ST-009 (terminal)
}

public static class RequisitionPriorities
{
    /// <summary>Oferecidas na tela: só Normal e Urgente (revisão de telas 2026-08-26).</summary>
    public static readonly string[] Offered = ["NORMAL", "URGENT"];

    /// <summary>Aceitas pela API — LOW/HIGH seguem válidas para as solicitações já gravadas.</summary>
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
    // Triagem de demandas (tickets): responsável designado pela continuidade após a aprovação
    public Guid? AssignedToId { get; set; }
    public string? AssignedToLabel { get; set; }
    public Guid? AssignedById { get; set; }
    public string? AssignedByLabel { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public List<RequisitionItem> Items { get; set; } = [];
    public List<RequisitionAttachment> Attachments { get; set; } = [];
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

/// <summary>Anexo da solicitação (PDF, imagem, planilha) — pedido na revisão de telas de 2026-08-26.</summary>
public class RequisitionAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequisitionId { get; set; }
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public Guid UploadedBy { get; set; }
    public string UploadedByLabel { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; }
}
