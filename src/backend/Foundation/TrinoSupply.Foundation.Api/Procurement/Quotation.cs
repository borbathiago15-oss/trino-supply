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
    PoIssued = 6,         // OC REGISTRADA (número vindo do ERP SENIOR)
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

    // ganho de negociação (revisão de telas 2026-08-26): o comprador negocia com o vencedor
    // e o sistema guarda quanto a negociação economizou em relação à primeira proposta dele
    public decimal? BaselineValue { get; set; }              // 1ª proposta do fornecedor escolhido
    public decimal? NegotiatedValue { get; set; }            // valor fechado depois da negociação
    public decimal? SavingValue { get; set; }                // baseline − fechado
    public decimal? SavingPercent { get; set; }
    public string? NegotiationNotes { get; set; }

    // ---- as outras duas réguas do saving (S.17) -----------------------------
    // Três coisas diferentes, três números com nome próprio. Somá-las ou trocar uma
    // pela outra faria o relatório dizer "saving" sem dizer de quê:
    //
    // | Régua | Mede |
    // |---|---|
    // | negociação (acima) | o que o comprador arrancou do MESMO fornecedor |
    // | competição | o que valeu ter chamado mais gente para o BID |
    // | orçamento | o quanto ficou abaixo do que o solicitante previa |

    /// <summary>Maior proposta comparável do BID — só de quem cotou a família inteira.</summary>
    public decimal? CompetitionBaselineValue { get; set; }
    /// <summary>Maior proposta − vencedora. Nulo quando só um fornecedor cotou: sem concorrência não há ganho de concorrência.</summary>
    public decimal? CompetitionSaving { get; set; }
    /// <summary>Orçamento das SCs de origem, congelado na adjudicação.</summary>
    public decimal? BudgetBaselineValue { get; set; }
    /// <summary>Orçamento − fechado. Nulo quando alguma SC do processo não informou orçamento.</summary>
    public decimal? BudgetSaving { get; set; }
    public string? NegotiatedByLabel { get; set; }
    public DateTimeOffset? NegotiatedAt { get; set; }

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
    /// <summary>Adjudicação por família (multi-fornecedor): uma linha por família cotada.</summary>
    public List<QuotationAward> Awards { get; set; } = [];

    /// <summary>Famílias em disputa no processo, na ordem em que aparecem nos itens.</summary>
    public IReadOnlyList<string> Families =>
        Items.OrderBy(i => i.Sequence).Select(i => QuotationAward.FamilyKey(i.Family))
            .Distinct().ToList();

    /// <summary>
    /// Adjudicações do processo, sem repetição e em ordem de família. Use SEMPRE esta lista:
    /// o EF pode ligar a mesma linha à coleção duas vezes quando ela é gravada e relida no
    /// mesmo contexto, e somar duas vezes a mesma família estouraria o valor da O.C.
    /// </summary>
    public IReadOnlyList<QuotationAward> AwardList =>
        Awards.DistinctBy(a => a.Id).OrderBy(a => a.Family).ToList();

    /// <summary>Fornecedores que ganharam alguma família — cada um recebe a sua O.C.</summary>
    public IReadOnlyList<Guid> AwardedSupplierIds =>
        AwardList.Select(a => a.SupplierId).Distinct().ToList();

    /// <summary>Mais de um fornecedor adjudicado: a compra se divide em várias O.C.s.</summary>
    public bool IsSplitAward => AwardedSupplierIds.Count > 1;

    /// <summary>SCs atendidas pelo processo: a primária mais as agrupadas via itens (V2 — regra 1 generalizada).</summary>
    public IReadOnlyList<Guid> SourcePrIds =>
        Items.Where(i => i.SourcePrId is not null).Select(i => i.SourcePrId!.Value)
            .Append(SourcePrId).Distinct().ToList();

    /// <summary>O processo cobre a SC? Vale tanto para a primária quanto para as agrupadas (exige Items carregados).</summary>
    public bool CoversPr(Guid prId) =>
        SourcePrId == prId || Items.Any(i => i.SourcePrId == prId);

    public IReadOnlyList<string> SourcePrNumbers =>
        Items.Where(i => !string.IsNullOrWhiteSpace(i.SourcePrNumber)).Select(i => i.SourcePrNumber!)
            .Append(SourcePrNumber).Distinct().ToList();
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
    /// <summary>
    /// Família do produto, em caixa alta (snapshot do catálogo). É o lote da adjudicação:
    /// cada família pode ficar com um fornecedor diferente. Item digitado (sem catálogo)
    /// cai em DIVERSOS. Cotações antigas ficam vazias e valem como uma família só.
    /// </summary>
    public string Family { get; set; } = string.Empty;
    // rastreio de origem (V2 — agrupamento multi-SC): de qual SC e de qual item da SC este item veio.
    // Cotações antigas ficam com null e continuam valendo pelo SourcePrId do cabeçalho.
    public Guid? SourcePrId { get; set; }
    public string? SourcePrNumber { get; set; }
    public Guid? SourcePrItemId { get; set; }
}

/// <summary>
/// Adjudicação de uma família a um fornecedor (RFQ-BR-005 estendida): a mesma compra pode ser
/// dividida entre vários fornecedores, um por família, cada um com a sua justificativa.
/// O rateio de frete/impostos/desconto da proposta acompanha a fatia ganha pelo fornecedor.
/// </summary>
public class QuotationAward
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuotationId { get; set; }
    public string Family { get; set; } = string.Empty;          // lote adjudicado (caixa alta)
    /// <summary>
    /// O item que esta adjudicação cobre, quando a divisão é <b>por item</b> — o caso de
    /// "o fornecedor A leva o papel e o B leva a caneta", dentro da mesma família.
    ///
    /// <para>
    /// <b>Nulo é a família inteira</b>, que é como toda adjudicação era antes e continua
    /// sendo quando um fornecedor leva o lote todo. Manter os dois significados na mesma
    /// coluna evita reescrever as adjudicações já gravadas: o que existe hoje continua
    /// válido e continua querendo dizer exatamente o que dizia.
    /// </para>
    /// </summary>
    public Guid? QuotationItemId { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;    // snapshot
    public Guid ProposalId { get; set; }
    public int ProposalVersion { get; set; }
    public decimal ItemsValue { get; set; }                     // soma dos itens da família
    public decimal TotalValue { get; set; }                     // itens + rateio de frete/impostos/outros − desconto
    public string? Criteria { get; set; }
    public string Justification { get; set; } = string.Empty;
    public Guid SelectedBy { get; set; }
    public string SelectedByLabel { get; set; } = string.Empty;
    public DateTimeOffset SelectedAt { get; set; }
    // O.C. do SENIOR que atende esta família (uma O.C. por fornecedor: famílias do mesmo
    // fornecedor compartilham o número)
    public Guid? PurchaseOrderId { get; set; }
    public string? PurchaseOrderNumber { get; set; }

    /// <summary>Chave da família: caixa alta, sem espaços nas pontas; vazio vira DIVERSOS.</summary>
    public const string Default = "DIVERSOS";
    public static string FamilyKey(string? family) =>
        string.IsNullOrWhiteSpace(family) ? Default : family.Trim().ToUpperInvariant();
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
    // Condição e forma são o nome do cadastro copiado para cá, não a chave dele: a
    // proposta é imutável, e renomear ou desativar o cadastro depois não pode mudar
    // o que o fornecedor propôs — é a mesma razão de SupplierName ser snapshot.
    public string? PaymentTerms { get; set; }                  // condição (ex.: 30/60 dias, à vista)
    public string? PaymentMethodName { get; set; }             // forma (boleto, Pix, depósito…)
    public int? PaymentDays { get; set; }                      // prazo para pagamento, em dias
    public decimal? FreightValue { get; set; }
    public decimal? TaxValue { get; set; }                    // impostos destacados na proposta (V2-P2)
    public decimal? OtherCosts { get; set; }                  // outros custos (embalagem, taxa…)
    public decimal? DiscountValue { get; set; }               // desconto negociado (mapa de cotação)
    public string Currency { get; set; } = "BRL";             // moeda da proposta
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
