using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Materials.Domain;

public readonly record struct StockRequestId(Guid Value)
{
    public static StockRequestId New() => new(Guid.NewGuid());
    public static StockRequestId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("StockRequestId não pode ser vazio.", nameof(value))
        : new StockRequestId(value);
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Ciclo de vida da solicitação de EPI/fardamento (spec Sistema de Almoxarifado).
/// Pendente → Aprovado/Rejeitado (gestor) → Em Separação/Solicitado Compra (almoxarifado) →
/// Em Rota → Entregue/Parcial (baixa no estoque) ou Cancelado.
/// </summary>
public enum RequestStatus
{
    Pendente = 1,
    Aprovado = 2,
    Rejeitado = 3,
    EmSeparacao = 4,
    SolicitadoCompra = 5,
    EmRota = 6,
    Entregue = 7,
    Parcial = 8,
    Cancelado = 9,
}

/// <summary>Motivos sugeridos da solicitação (spec Almoxarifado). Aceita texto livre além destes.</summary>
public static class RequestReasons
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "Troca de tamanho", "Danificado", "Roteiro mensal", "Perda", "Roubo", "Troca programada",
    };
}

/// <summary>Item solicitado (produto/tamanho via SKU + quantidade). Imutável após a criação (compliance).</summary>
public sealed class StockRequestLine : IBelongsToTenant
{
    private StockRequestLine(Guid id, CompanyId companyId, StockRequestId requestId, string itemCode, decimal quantity)
    {
        Id = id;
        CompanyId = companyId;
        RequestId = requestId;
        ItemCode = itemCode;
        Quantity = quantity;
    }

    private StockRequestLine() { } // EF

    public Guid Id { get; private set; }
    public CompanyId CompanyId { get; private set; }
    public StockRequestId RequestId { get; private set; }
    public string ItemCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }

    public static StockRequestLine Create(CompanyId companyId, StockRequestId requestId, string itemCode, decimal quantity) =>
        new(Guid.NewGuid(), companyId, requestId, itemCode.Trim().ToUpperInvariant(), quantity);
}

/// <summary>
/// Solicitação de material de estoque (EPI/fardamento) — Fluxo A do almoxarifado. O solicitante pede
/// produtos ligados a um centro de custo e a um gestor aprovador, com um motivo. Após a aprovação do
/// gestor, o almoxarifado separa (se há estoque) ou solicita compra (se falta), despacha e entrega —
/// dando baixa no estoque. A solicitação inicial é imutável (garante compliance).
/// </summary>
public sealed class StockRequest : AggregateRoot<StockRequestId>, IBelongsToTenant
{
    private StockRequest(StockRequestId id, CompanyId companyId, string requesterSubject, string companyCode,
        string costCenterCode, string managerSubject, string reason, DateTimeOffset createdAt) : base(id)
    {
        CompanyId = companyId;
        RequesterSubject = requesterSubject;
        CompanyCode = companyCode;
        CostCenterCode = costCenterCode;
        ManagerSubject = managerSubject;
        Reason = reason;
        Status = RequestStatus.Pendente;
        CreatedAt = createdAt;
    }

    private StockRequest() : base(default!) { } // EF

    private readonly List<StockRequestLine> _lines = new();

    public CompanyId CompanyId { get; private set; }
    public string RequesterSubject { get; private set; } = string.Empty;
    public string CompanyCode { get; private set; } = string.Empty;
    public string CostCenterCode { get; private set; } = string.Empty;
    public string ManagerSubject { get; private set; } = string.Empty;  // gestor aprovador
    public string Reason { get; private set; } = string.Empty;
    public RequestStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? DecisionBySubject { get; private set; }
    public DateTimeOffset? DecisionAt { get; private set; }
    public string? DecisionNote { get; private set; }
    public IReadOnlyList<StockRequestLine> Lines => _lines;

    public static Result<StockRequest> Create(CompanyId companyId, string requesterSubject, string companyCode,
        string costCenterCode, string managerSubject, string reason,
        IEnumerable<(string ItemCode, decimal Quantity)> lines, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(requesterSubject))
            return Result.Failure<StockRequest>(new Error("warehouse.requester_required", "Solicitante é obrigatório."));
        if (string.IsNullOrWhiteSpace(companyCode))
            return Result.Failure<StockRequest>(new Error("warehouse.company_required", "Informe a empresa."));
        if (string.IsNullOrWhiteSpace(costCenterCode))
            return Result.Failure<StockRequest>(new Error("warehouse.cost_center_required", "Informe o centro de custo."));
        if (string.IsNullOrWhiteSpace(managerSubject))
            return Result.Failure<StockRequest>(new Error("warehouse.manager_required", "Informe o gestor aprovador."));
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure<StockRequest>(new Error("warehouse.reason_required", "Informe o motivo da solicitação."));

        var list = lines?.ToList() ?? new();
        if (list.Count == 0)
            return Result.Failure<StockRequest>(new Error("warehouse.lines_required", "Informe ao menos um produto."));
        if (list.Any(l => l.Quantity <= 0))
            return Result.Failure<StockRequest>(new Error("warehouse.qty_invalid", "Quantidade deve ser positiva."));
        if (list.Any(l => string.IsNullOrWhiteSpace(l.ItemCode)))
            return Result.Failure<StockRequest>(new Error("warehouse.item_required", "Produto é obrigatório em todas as linhas."));

        var req = new StockRequest(StockRequestId.New(), companyId, requesterSubject.Trim(),
            companyCode.Trim().ToUpperInvariant(), costCenterCode.Trim().ToUpperInvariant(),
            managerSubject.Trim(), reason.Trim(), createdAt);
        foreach (var l in list)
            req._lines.Add(StockRequestLine.Create(companyId, req.Id, l.ItemCode, l.Quantity));
        return Result.Success(req);
    }

    private Result Guard(string subject, RequestStatus expected)
    {
        if (Status != expected)
            return Result.Failure(new Error("warehouse.invalid_state", $"Ação inválida no estado {Status}."));
        return Result.Success();
    }

    public Result Approve(string managerSubject, DateTimeOffset now)
    {
        var g = Guard(managerSubject, RequestStatus.Pendente);
        if (g.IsFailure) return g;
        if (!string.Equals(managerSubject, ManagerSubject, StringComparison.Ordinal))
            return Result.Failure(new Error("warehouse.wrong_manager", "Apenas o gestor designado pode aprovar."));
        Status = RequestStatus.Aprovado;
        Decide(managerSubject, now, null);
        return Result.Success();
    }

    public Result Reject(string managerSubject, string? note, DateTimeOffset now)
    {
        var g = Guard(managerSubject, RequestStatus.Pendente);
        if (g.IsFailure) return g;
        if (!string.Equals(managerSubject, ManagerSubject, StringComparison.Ordinal))
            return Result.Failure(new Error("warehouse.wrong_manager", "Apenas o gestor designado pode rejeitar."));
        if (string.IsNullOrWhiteSpace(note))
            return Result.Failure(new Error("warehouse.reject_note_required", "Informe o motivo da rejeição."));
        Status = RequestStatus.Rejeitado;
        Decide(managerSubject, now, note.Trim());
        return Result.Success();
    }

    /// <summary>Almoxarifado inicia a separação: em estoque → Em Separação; senão → Solicitado Compra.</summary>
    public Result StartSeparation(bool inStock, DateTimeOffset now)
    {
        if (Status != RequestStatus.Aprovado && Status != RequestStatus.SolicitadoCompra)
            return Result.Failure(new Error("warehouse.invalid_state", $"Separação inválida no estado {Status}."));
        Status = inStock ? RequestStatus.EmSeparacao : RequestStatus.SolicitadoCompra;
        Version++;
        return Result.Success();
    }

    public Result Dispatch(DateTimeOffset now)
    {
        var g = Guard("", RequestStatus.EmSeparacao);
        if (g.IsFailure) return g;
        Status = RequestStatus.EmRota;
        Version++;
        return Result.Success();
    }

    /// <summary>Entrega: total → Entregue; parcial → Parcial. A baixa no estoque é feita pelo serviço.</summary>
    public Result Deliver(bool partial, DateTimeOffset now)
    {
        if (Status != RequestStatus.EmRota && Status != RequestStatus.EmSeparacao)
            return Result.Failure(new Error("warehouse.invalid_state", $"Entrega inválida no estado {Status}."));
        Status = partial ? RequestStatus.Parcial : RequestStatus.Entregue;
        Version++;
        return Result.Success();
    }

    public Result Cancel(string subject, string? note, DateTimeOffset now)
    {
        if (Status is RequestStatus.Entregue or RequestStatus.Cancelado)
            return Result.Failure(new Error("warehouse.invalid_state", $"Não é possível cancelar no estado {Status}."));
        Status = RequestStatus.Cancelado;
        Decide(subject, now, string.IsNullOrWhiteSpace(note) ? null : note.Trim());
        return Result.Success();
    }

    private void Decide(string subject, DateTimeOffset now, string? note)
    {
        DecisionBySubject = subject;
        DecisionAt = now;
        DecisionNote = note;
        Version++;
    }
}
