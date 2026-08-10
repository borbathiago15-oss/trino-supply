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

public enum RequisitionStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4
}

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

    public static RequisitionLine Create(CompanyId companyId, RequisitionId requisitionId, string itemCode, decimal quantity, string unit) =>
        new(Guid.NewGuid(), companyId, requisitionId, itemCode.Trim().ToUpperInvariant(), quantity, unit.Trim().ToLowerInvariant());
}

/// <summary>
/// Requisição de compra (PR-001). Fluxo: Draft → Submitted → Approved/Rejected. Aplica
/// <b>Segregation of Duties</b>: quem requisita NÃO pode aprovar a própria requisição (SEC-001).
/// A decisão usa concorrência otimista (<see cref="Entity{TId}.Version"/>) — só uma decisão vinga.
/// </summary>
public sealed class PurchaseRequisition : AggregateRoot<RequisitionId>, IBelongsToTenant
{
    private readonly List<RequisitionLine> _lines = new();

    private PurchaseRequisition(RequisitionId id, CompanyId companyId, string requesterSubject, DateTimeOffset createdAt)
        : base(id)
    {
        CompanyId = companyId;
        RequesterSubject = requesterSubject;
        CreatedAt = createdAt;
        Status = RequisitionStatus.Draft;
    }

    private PurchaseRequisition() : base(default!) { } // EF

    public CompanyId CompanyId { get; private set; }
    public string RequesterSubject { get; private set; } = string.Empty;
    public RequisitionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? DecidedBySubject { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionNote { get; private set; }
    public IReadOnlyList<RequisitionLine> Lines => _lines;

    public static Result<PurchaseRequisition> Create(
        CompanyId companyId, string requesterSubject, IEnumerable<(string ItemCode, decimal Quantity, string Unit)> lines,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(requesterSubject))
            return Result.Failure<PurchaseRequisition>(new Error("purchases.requester_required", "Requisitante obrigatório."));

        var materialized = lines.ToList();
        if (materialized.Count == 0)
            return Result.Failure<PurchaseRequisition>(new Error("purchases.lines_required", "A requisição precisa de ao menos uma linha."));
        if (materialized.Any(l => l.Quantity <= 0))
            return Result.Failure<PurchaseRequisition>(new Error("purchases.qty_invalid", "Quantidade das linhas deve ser positiva."));

        var req = new PurchaseRequisition(RequisitionId.New(), companyId, requesterSubject, createdAt);
        foreach (var l in materialized)
            req._lines.Add(RequisitionLine.Create(companyId, req.Id, l.ItemCode, l.Quantity, l.Unit));
        return Result.Success(req);
    }

    /// <summary>Acrescenta itens a um rascunho (item manual na tela ou importação em lote via planilha).</summary>
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

    public Result Approve(string approverSubject, DateTimeOffset now)
    {
        var guard = GuardDecision(approverSubject);
        if (guard.IsFailure) return guard;

        Status = RequisitionStatus.Approved;
        Decide(approverSubject, now, null);
        return Result.Success();
    }

    public Result Reject(string approverSubject, string? note, DateTimeOffset now)
    {
        var guard = GuardDecision(approverSubject);
        if (guard.IsFailure) return guard;

        Status = RequisitionStatus.Rejected;
        Decide(approverSubject, now, note);
        return Result.Success();
    }

    private Result GuardDecision(string approverSubject)
    {
        if (Status != RequisitionStatus.Submitted)
            return Result.Failure(new Error("purchases.not_submitted", "Só requisições enviadas podem ser decididas."));
        // Segregation of Duties: aprovador não pode ser o requisitante.
        if (string.Equals(approverSubject, RequesterSubject, StringComparison.Ordinal))
            return Result.Failure(new Error("purchases.sod_violation", "Segregação de funções: o requisitante não pode aprovar a própria requisição."));
        return Result.Success();
    }

    private void Decide(string approverSubject, DateTimeOffset now, string? note)
    {
        DecidedBySubject = approverSubject;
        DecidedAt = now;
        DecisionNote = note;
        Version++;
    }
}
