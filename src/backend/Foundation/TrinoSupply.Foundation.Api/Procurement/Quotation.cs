namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// Cotação/BID (RFQ-001): elo obrigatório entre a requisição aprovada e a ordem de compra.
/// Cotação, aprovação e emissão da OC não existem como processos independentes —
/// toda transição muda a responsabilidade automaticamente e gera evento de timeline.
/// </summary>
public enum QuotationKind : short
{
    Purchase = 1,   // cotação de compra
    Service = 2,    // cotação de serviço
    Bid = 3,        // BID
}

public enum QuotationStatus : short
{
    Open = 1,             // COTAÇÃO ABERTA / AGUARDANDO PROPOSTAS
    Analysis = 2,         // PROPOSTAS RECEBIDAS / EM ANÁLISE
    AwaitingManager = 3,  // FORNECEDOR SELECIONADO → AGUARDANDO APROVAÇÃO GERENCIAL
    AwaitingDirector = 4, // AGUARDANDO APROVAÇÃO DIRETORIA
    ApprovedForIssue = 5, // APROVADO PARA EMISSÃO DA OC
    PoIssued = 6,         // OC EMITIDA
    Rejected = 7,         // REJEITADO (motivo obrigatório)
    Cancelled = 8,        // CANCELADA (motivo obrigatório)
}

public class Quotation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = string.Empty;      // RFQ-2026-000001 | BID-2026-000001 (RFQ-BR-002)
    public QuotationKind Kind { get; set; } = QuotationKind.Purchase;
    public QuotationStatus Status { get; set; } = QuotationStatus.Open;
    public Guid SourcePrId { get; set; }                    // sempre nasce de PR aprovada (RFQ-BR-001)
    public string SourcePrNumber { get; set; } = string.Empty;
    public string CostCenter { get; set; } = string.Empty;  // snapshot da PR
    public string Justification { get; set; } = string.Empty; // snapshot da PR
    public DateOnly? Deadline { get; set; }                 // prazo para resposta dos fornecedores
    public string? Notes { get; set; }

    // escolha do fornecedor (RFQ-BR-005)
    public Guid? WinnerSupplierId { get; set; }
    public Guid? WinnerProposalId { get; set; }
    public string? SelectionCriteria { get; set; }          // CSV dos critérios utilizados
    public string? SelectionJustification { get; set; }
    public Guid? SelectedBy { get; set; }
    public string? SelectedByLabel { get; set; }
    public DateTimeOffset? SelectedAt { get; set; }

    // alçadas (RFQ-BR-006/007)
    public Guid? ManagerApprovedBy { get; set; }
    public string? ManagerApprovedByLabel { get; set; }
    public DateTimeOffset? ManagerApprovedAt { get; set; }
    public Guid? DirectorApprovedBy { get; set; }
    public string? DirectorApprovedByLabel { get; set; }
    public DateTimeOffset? DirectorApprovedAt { get; set; }
    public string? DecisionReason { get; set; }             // último motivo de ajuste/rejeição/cancelamento

    public Guid? PurchaseOrderId { get; set; }
    public string? PurchaseOrderNumber { get; set; }

    public Guid CreatedBy { get; set; }
    public string CreatedByLabel { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;

    public List<QuotationItem> Items { get; set; } = [];
    public List<QuotationSupplier> Suppliers { get; set; } = [];
    public List<Proposal> Proposals { get; set; } = [];
}

public class QuotationItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuotationId { get; set; }
    public int Sequence { get; set; }
    public Guid? CatalogItemId { get; set; }
    public string? CatalogCode { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string UnitOfMeasure { get; set; } = "UN";
}

/// <summary>Fornecedor convidado (somente ativos do cadastro único — RFQ-BR-003).</summary>
public class QuotationSupplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuotationId { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;  // snapshot
    public string TaxId { get; set; } = string.Empty;         // snapshot
    public Guid InvitedBy { get; set; }
    public string InvitedByLabel { get; set; } = string.Empty;
    public DateTimeOffset InvitedAt { get; set; }
}

/// <summary>Proposta comercial — imutável e versionada; nova versão supersede (RFQ-BR-004).</summary>
public class Proposal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuotationId { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;  // snapshot
    public int VersionNumber { get; set; } = 1;
    public decimal TotalValue { get; set; }
    public int? DeliveryDays { get; set; }
    public string? PaymentTerms { get; set; }
    public decimal? FreightValue { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public string? Notes { get; set; }
    public string SubmittedVia { get; set; } = "PORTAL";      // PORTAL | INTERNO
    public string SubmittedByLabel { get; set; } = string.Empty;
    public DateTimeOffset SubmittedAt { get; set; }
    public Guid? AttachmentDocumentId { get; set; }
    public string? AttachmentFileName { get; set; }
    public List<ProposalItem> Items { get; set; } = [];
}

public class ProposalItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProposalId { get; set; }
    public Guid QuotationItemId { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; }
}

/// <summary>Evento da timeline do processo — nunca excluído (RFQ-BR-009).</summary>
public class ProcessEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuotationId { get; set; }
    public string EventType { get; set; } = string.Empty;     // ex.: COTACAO_ABERTA, FORNECEDOR_CONVIDADO…
    public string Description { get; set; } = string.Empty;
    public QuotationStatus? FromStatus { get; set; }
    public QuotationStatus? ToStatus { get; set; }
    public Guid? ActorId { get; set; }
    public string ActorLabel { get; set; } = string.Empty;
    public string? Note { get; set; }
    public Guid? DocumentId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
