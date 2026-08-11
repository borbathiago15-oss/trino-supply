using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Materials.Application;

public sealed record StockRequestLineInput(string ItemCode, decimal Quantity);
public sealed record StockRequestLineView(string ItemCode, decimal Quantity, decimal Balance);

/// <summary>Nova solicitação de EPI/fardamento (Fluxo A do almoxarifado).</summary>
public sealed record CreateStockRequestInput(
    string CompanyCode, string CostCenterCode, string ManagerSubject, string Reason,
    IReadOnlyList<StockRequestLineInput> Lines);

public sealed record StockRequestView(
    Guid Id, string RequesterSubject, string CompanyCode, string CostCenterCode, string ManagerSubject,
    string Reason, string Status, DateTimeOffset CreatedAt, string? DecisionBy, DateTimeOffset? DecisionAt,
    string? DecisionNote, Guid? LinkedRequisitionId, IReadOnlyList<StockRequestLineView> Lines);

/// <summary>
/// Solicitação de material de estoque (Fluxo A). Fluxo: Pendente → Aprovado/Rejeitado (gestor) →
/// Em Separação/Solicitado Compra (almoxarifado) → Em Rota → Entregue/Parcial (baixa) / Cancelado.
/// </summary>
public interface IStockRequestService
{
    Task<Result<Guid>> CreateAsync(CreateStockRequestInput input, CancellationToken ct = default);
    /// <summary><paramref name="all"/> = visão do almoxarifado (todas); senão, apenas as do solicitante/gestor.</summary>
    Task<IReadOnlyList<StockRequestView>> ListAsync(bool all, int limit = 200, CancellationToken ct = default);
    /// <summary>Central de Aprovação: solicitações pendentes onde o usuário corrente é o gestor (no escopo).</summary>
    Task<IReadOnlyList<StockRequestView>> ListMyApprovalsAsync(CancellationToken ct = default);
    Task<Result> ApproveAsync(Guid id, CancellationToken ct = default);
    Task<Result> RejectAsync(Guid id, string? note, CancellationToken ct = default);
    /// <summary>Almoxarifado inicia a separação: verifica o estoque de todas as linhas.</summary>
    Task<Result> StartSeparationAsync(Guid id, CancellationToken ct = default);
    Task<Result> DispatchAsync(Guid id, CancellationToken ct = default);
    /// <summary>Entrega: dá baixa no estoque das linhas e conclui (total → Entregue).</summary>
    Task<Result> DeliverAsync(Guid id, CancellationToken ct = default);
    Task<Result> CancelAsync(Guid id, string? note, CancellationToken ct = default);

    Task<Result<StockRequestView>> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Ponte v3: vincula o pedido de compra gerado (uma única vez, em Solicitado Compra).</summary>
    Task<Result> MarkPurchaseGeneratedAsync(Guid id, Guid requisitionId, CancellationToken ct = default);
}
