using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Procurement.Domain;

public readonly record struct RequisitionId(Guid Value)
{
    public static RequisitionId New() => new(Guid.NewGuid());
    public static RequisitionId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("RequisitionId não pode ser vazio.", nameof(value))
        : new RequisitionId(value);
    public override string ToString() => Value.ToString();
}

/// <summary>Situação da solicitação (spec Sistema de Compras) — aprovação em dois níveis.</summary>
public enum RequisitionStatus
{
    Draft = 1,
    Submitted = 2,          // aguardando aprovação nível 1
    ApprovedLevel1 = 3,     // nível 1 liberou o custo; aguardando nível 2
    Approved = 4,           // aprovado (nível 2) — segue para compra (OC)
    Rejected = 5,
    FulfilledFromStock = 6, // aprovado e atendido pelo ESTOQUE INTERNO (baixa no Almox) — v2, sem OC
    PartiallyOrdered = 7,   // parte das linhas já virou OC; ainda há itens sem pedido — v3 (multi-OC)
    Ordered = 8,            // todas as linhas cobertas por OCs emitidas — v3 (multi-OC)
}

/// <summary>Tipo de demanda / prioridade da solicitação.</summary>
public enum RequisitionPriority { Normal = 1, Emergencial = 2 }

/// <summary>Linha de requisição: item (código snapshot), quantidade e unidade. Escopada ao tenant (RLS).</summary>
public sealed class RequisitionLine : IBelongsToTenant
{
    private RequisitionLine(Guid id, CompanyId companyId, RequisitionId requisitionId, string itemCode, decimal quantity, string unit)
    {
        Id = id;
        CompanyId = companyId;
        RequisitionId = requisitionId;
        ItemCode = itemCode;
        Quantity = quantity;
        Unit = unit;
    }

    private RequisitionLine() { } // EF

    public Guid Id { get; private set; }
    public CompanyId CompanyId { get; private set; }
    public RequisitionId RequisitionId { get; private set; }
    public string ItemCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string Unit { get; private set; } = string.Empty;

    /// <summary>
    /// OC que cobre esta linha (v3 — compra dividida entre fornecedores). Nula enquanto o item não foi
    /// pedido. É a rastreabilidade item→OC e a trava que impede a mesma linha entrar em duas OCs.
    /// </summary>
    public Guid? PurchaseOrderId { get; private set; }

    /// <summary>Linha ainda sem pedido emitido — elegível para entrar numa próxima OC.</summary>
    public bool IsPending => PurchaseOrderId is null;

    internal void AssignOrder(Guid orderId) => PurchaseOrderId = orderId;

    /// <summary>Devolve a linha para "a pedir" quando a OC que a cobria é cancelada.</summary>
    internal void ReleaseOrder() => PurchaseOrderId = null;

    public static RequisitionLine Create(CompanyId companyId, RequisitionId requisitionId, string itemCode, decimal quantity, string unit) =>
        new(Guid.NewGuid(), companyId, requisitionId, itemCode.Trim().ToUpperInvariant(), quantity, unit.Trim().ToLowerInvariant());
}

/// <summary>
/// Solicitação de compra (PR-001 / spec Sistema de Compras). Cabeçalho com <b>empresa do custo</b>
/// (pagadora), <b>centro de custo</b>, <b>prioridade</b> e <b>justificativa</b> obrigatória. Fluxo:
/// Draft → Submitted → ApprovedLevel1 → Approved / Rejected, com <b>aprovação em dois níveis</b> e
/// <b>Segregation of Duties</b> (aprovadores ≠ requisitante; cada nível só é decidido pelo aprovador
/// designado). A decisão usa concorrência otimista (<see cref="Entity{TId}.Version"/>).
/// </summary>
public sealed class PurchaseRequisition : AggregateRoot<RequisitionId>, IBelongsToTenant
{
    private readonly List<RequisitionLine> _lines = new();

    private PurchaseRequisition(
        RequisitionId id, CompanyId companyId, string requesterSubject, PayingCompanyId payingCompanyId,
        CostCenterId costCenterId, RequisitionPriority priority, string justification,
        string approverLevel1Subject, string approverLevel2Subject, DateTimeOffset createdAt) : base(id)
    {
        CompanyId = companyId;
        RequesterSubject = requesterSubject;
        PayingCompanyId = payingCompanyId;
        CostCenterId = costCenterId;
        Priority = priority;
        Justification = justification;
        ApproverLevel1Subject = approverLevel1Subject;
        ApproverLevel2Subject = approverLevel2Subject;
        CreatedAt = createdAt;
        Status = RequisitionStatus.Draft;
    }

    private PurchaseRequisition() : base(default!) { } // EF

    public CompanyId CompanyId { get; private set; }
    public string RequesterSubject { get; private set; } = string.Empty;
    public PayingCompanyId PayingCompanyId { get; private set; }   // empresa do custo (faturamento)
    public CostCenterId CostCenterId { get; private set; }
    public RequisitionPriority Priority { get; private set; }
    public string Justification { get; private set; } = string.Empty;
    public string ApproverLevel1Subject { get; private set; } = string.Empty;
    public string ApproverLevel2Subject { get; private set; } = string.Empty;
    public RequisitionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? Level1DecidedBySubject { get; private set; }
    public DateTimeOffset? Level1DecidedAt { get; private set; }
    public string? Level2DecidedBySubject { get; private set; }
    public DateTimeOffset? Level2DecidedAt { get; private set; }
    public string? RejectedBySubject { get; private set; }
    public DateTimeOffset? RejectedAt { get; private set; }
    public string? DecisionNote { get; private set; } // justificativa da rejeição
    public IReadOnlyList<RequisitionLine> Lines => _lines;

    public static Result<PurchaseRequisition> Create(
        CompanyId companyId, string requesterSubject, PayingCompanyId payingCompanyId, CostCenterId costCenterId,
        RequisitionPriority priority, string justification, string approverLevel1Subject, string approverLevel2Subject,
        IEnumerable<(string ItemCode, decimal Quantity, string Unit)> lines, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(requesterSubject))
            return Result.Failure<PurchaseRequisition>(new Error("purchases.requester_required", "Requisitante obrigatório."));
        if (string.IsNullOrWhiteSpace(justification))
            return Result.Failure<PurchaseRequisition>(new Error("purchases.justification_required", "Justificativa da solicitação é obrigatória."));
        if (string.IsNullOrWhiteSpace(approverLevel1Subject) || string.IsNullOrWhiteSpace(approverLevel2Subject))
            return Result.Failure<PurchaseRequisition>(new Error("purchases.approvers_required", "Informe os aprovadores nível 1 e nível 2."));

        var l1 = approverLevel1Subject.Trim();
        var l2 = approverLevel2Subject.Trim();
        // Segregation of Duties: aprovadores não podem ser o requisitante, e os níveis devem ser distintos.
        if (string.Equals(l1, requesterSubject, StringComparison.Ordinal) || string.Equals(l2, requesterSubject, StringComparison.Ordinal))
            return Result.Failure<PurchaseRequisition>(new Error("purchases.sod_violation", "Segregação de funções: o requisitante não pode aprovar a própria solicitação."));
        if (string.Equals(l1, l2, StringComparison.Ordinal))
            return Result.Failure<PurchaseRequisition>(new Error("purchases.approvers_distinct", "Os aprovadores de nível 1 e nível 2 devem ser diferentes."));

        var materialized = lines.ToList();
        if (materialized.Count == 0)
            return Result.Failure<PurchaseRequisition>(new Error("purchases.lines_required", "A requisição precisa de ao menos uma linha."));
        if (materialized.Any(l => l.Quantity <= 0))
            return Result.Failure<PurchaseRequisition>(new Error("purchases.qty_invalid", "Quantidade das linhas deve ser positiva."));

        var req = new PurchaseRequisition(
            RequisitionId.New(), companyId, requesterSubject, payingCompanyId, costCenterId, priority,
            justification.Trim(), l1, l2, createdAt);
        foreach (var l in materialized)
            req._lines.Add(RequisitionLine.Create(companyId, req.Id, l.ItemCode, l.Quantity, l.Unit));
        return Result.Success(req);
    }

    /// <summary>Acrescenta itens a um rascunho (item manual na tela ou importação em lote).</summary>
    public Result AddLines(IEnumerable<(string ItemCode, decimal Quantity, string Unit)> lines)
    {
        if (Status != RequisitionStatus.Draft)
            return Result.Failure(new Error("purchases.not_draft", "Só rascunhos podem receber novos itens."));

        var materialized = lines.ToList();
        if (materialized.Count == 0)
            return Result.Failure(new Error("purchases.lines_required", "Informe ao menos um item."));
        if (materialized.Any(l => l.Quantity <= 0))
            return Result.Failure(new Error("purchases.qty_invalid", "Quantidade das linhas deve ser positiva."));

        foreach (var l in materialized)
            _lines.Add(RequisitionLine.Create(CompanyId, Id, l.ItemCode, l.Quantity, l.Unit));
        Version++;
        return Result.Success();
    }

    public Result Submit()
    {
        if (Status != RequisitionStatus.Draft)
            return Result.Failure(new Error("purchases.not_draft", "Só rascunhos podem ser enviados."));
        if (_lines.Count == 0)
            return Result.Failure(new Error("purchases.lines_required", "A requisição precisa de ao menos uma linha."));
        Status = RequisitionStatus.Submitted;
        Version++;
        return Result.Success();
    }

    /// <summary>Aprovação nível 1 (revisão/liberação do custo) — só pelo aprovador designado.</summary>
    public Result ApproveLevel1(string approverSubject, DateTimeOffset now)
    {
        if (Status != RequisitionStatus.Submitted)
            return Result.Failure(new Error("purchases.not_submitted", "Só solicitações enviadas podem ser aprovadas no nível 1."));
        if (!string.Equals(approverSubject, ApproverLevel1Subject, StringComparison.Ordinal))
            return Result.Failure(new Error("purchases.wrong_approver", "Somente o aprovador de nível 1 designado pode aprovar esta etapa."));

        Status = RequisitionStatus.ApprovedLevel1;
        Level1DecidedBySubject = approverSubject;
        Level1DecidedAt = now;
        Version++;
        return Result.Success();
    }

    /// <summary>Aprovação nível 2 (final) — só após o nível 1 e pelo aprovador designado.</summary>
    public Result ApproveLevel2(string approverSubject, DateTimeOffset now)
    {
        if (Status != RequisitionStatus.ApprovedLevel1)
            return Result.Failure(new Error("purchases.not_level1", "O nível 2 só aprova após a aprovação do nível 1."));
        if (!string.Equals(approverSubject, ApproverLevel2Subject, StringComparison.Ordinal))
            return Result.Failure(new Error("purchases.wrong_approver", "Somente o aprovador de nível 2 designado pode aprovar esta etapa."));

        Status = RequisitionStatus.Approved;
        Level2DecidedBySubject = approverSubject;
        Level2DecidedAt = now;
        Version++;
        return Result.Success();
    }

    /// <summary>
    /// Roteamento pós-aprovação (v2): o pedido aprovado foi atendido pelo estoque interno do Almox
    /// (baixa já efetuada) — encerra sem OC. Só a partir de <see cref="RequisitionStatus.Approved"/>.
    /// </summary>
    public Result MarkFulfilledFromStock(DateTimeOffset now)
    {
        if (Status != RequisitionStatus.Approved)
            return Result.Failure(new Error("purchases.not_approved", "Só um pedido aprovado pode ser atendido pelo estoque."));
        Status = RequisitionStatus.FulfilledFromStock;
        Version++;
        return Result.Success();
    }

    /// <summary>
    /// Vincula as linhas informadas à OC recém-emitida (v3 — compra dividida por fornecedor). Uma
    /// requisição pode gerar VÁRIAS OCs, cada uma cobrindo um subconjunto de itens; a linha que já
    /// está em uma OC não entra em outra. O status passa a <see cref="RequisitionStatus.PartiallyOrdered"/>
    /// enquanto sobrar item a pedir, e a <see cref="RequisitionStatus.Ordered"/> quando todos forem cobertos.
    /// </summary>
    public Result MarkLinesOrdered(IReadOnlyCollection<Guid> lineIds, Guid orderId)
    {
        if (Status is not (RequisitionStatus.Approved or RequisitionStatus.PartiallyOrdered))
            return Result.Failure(new Error("purchases.order.not_approved",
                "Só requisições aprovadas (ou parcialmente atendidas) geram pedido."));
        if (lineIds.Count == 0)
            return Result.Failure(new Error("purchases.order.lines_required", "Informe ao menos um item para a OC."));

        var alvo = new List<RequisitionLine>(lineIds.Count);
        foreach (var lineId in lineIds)
        {
            var line = _lines.FirstOrDefault(l => l.Id == lineId);
            if (line is null)
                return Result.Failure(new Error("purchases.order.line_not_found", "Item informado não pertence a esta requisição."));
            if (!line.IsPending)
                return Result.Failure(new Error("purchases.order.line_already_ordered",
                    $"O item '{line.ItemCode}' já está em outra OC desta requisição."));
            alvo.Add(line);
        }

        foreach (var line in alvo) line.AssignOrder(orderId);
        Status = _lines.All(l => !l.IsPending) ? RequisitionStatus.Ordered : RequisitionStatus.PartiallyOrdered;
        Version++;
        return Result.Success();
    }

    /// <summary>
    /// Devolve à fila de compra as linhas cobertas por uma OC cancelada (v3). A requisição volta a
    /// <see cref="RequisitionStatus.PartiallyOrdered"/> — ou a <see cref="RequisitionStatus.Approved"/>
    /// se nenhuma linha restar pedida.
    /// </summary>
    public Result ReleaseOrderLines(Guid orderId)
    {
        var afetadas = _lines.Where(l => l.PurchaseOrderId == orderId).ToList();
        if (afetadas.Count == 0) return Result.Success(); // idempotente: OC sem linhas vinculadas

        foreach (var line in afetadas) line.ReleaseOrder();
        Status = _lines.All(l => l.IsPending) ? RequisitionStatus.Approved : RequisitionStatus.PartiallyOrdered;
        Version++;
        return Result.Success();
    }

    /// <summary>Rejeição (justificativa obrigatória) pelo aprovador da etapa atual (nível 1 ou 2).</summary>
    public Result Reject(string approverSubject, string? note, DateTimeOffset now)
    {
        var expected = Status switch
        {
            RequisitionStatus.Submitted => ApproverLevel1Subject,
            RequisitionStatus.ApprovedLevel1 => ApproverLevel2Subject,
            _ => null
        };
        if (expected is null)
            return Result.Failure(new Error("purchases.not_submitted", "Só solicitações em aprovação podem ser rejeitadas."));
        if (!string.Equals(approverSubject, expected, StringComparison.Ordinal))
            return Result.Failure(new Error("purchases.wrong_approver", "Somente o aprovador da etapa atual pode rejeitar."));
        if (string.IsNullOrWhiteSpace(note))
            return Result.Failure(new Error("purchases.reject_note_required", "A justificativa da rejeição é obrigatória."));

        Status = RequisitionStatus.Rejected;
        DecisionNote = note.Trim();
        RejectedBySubject = approverSubject;
        RejectedAt = now;
        Version++;
        return Result.Success();
    }
}
