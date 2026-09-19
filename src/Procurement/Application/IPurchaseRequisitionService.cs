using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Procurement.Application;

public sealed record RequisitionLineInput(string ItemCode, decimal Quantity, string Unit);
/// <summary>Linha da requisição. <c>PurchaseOrderId</c> nulo = item ainda a pedir (v3: compra dividida).</summary>
public sealed record RequisitionLineView(string ItemCode, decimal Quantity, string Unit, Guid? PurchaseOrderId);

/// <summary>
/// Dados da nova solicitação (spec Sistema de Compras): empresa do custo (pagadora), centro de custo,
/// prioridade (Normal/Emergencial), justificativa e os aprovadores nível 1 e nível 2.
/// </summary>
public sealed record CreateRequisitionInput(
    string PayingCompanyCode, string CostCenterCode, string Priority, string Justification,
    string ApproverLevel1Subject, string ApproverLevel2Subject, IReadOnlyList<RequisitionLineInput> Lines);

/// <summary>Tempo de ciclo dos pedidos na janela: médias em horas (criação → decisão de cada nível).</summary>
public sealed record CycleStatsView(
    int Total, int Approved, int Rejected, int FulfilledFromStock, int Pending,
    double? AvgHoursToLevel1, double? AvgHoursToLevel2);

public sealed record RequisitionView(
    Guid Id, string Requester, string Status, DateTimeOffset CreatedAt,
    string PayingCompanyCode, string PayingCompanyName, string CostCenterCode, string CostCenterName,
    string Priority, string Justification, string ApproverLevel1, string ApproverLevel2,
    string? Level1DecidedBy, string? Level2DecidedBy, string? RejectedBy, string? DecisionNote,
    IReadOnlyList<RequisitionLineView> Lines);

/// <summary>
/// Casos de uso da solicitação de compra (PR-001). Requisitante vem do JWT; a aprovação é em
/// <b>dois níveis</b> com Segregation of Duties aplicada no domínio.
/// </summary>
public interface IPurchaseRequisitionService
{
    Task<Result<Guid>> CreateAsync(CreateRequisitionInput input, CancellationToken ct = default);
    /// <summary>Acrescenta itens a um rascunho (item manual na tela ou importação em lote).</summary>
    Task<Result> AddLinesAsync(Guid id, IReadOnlyList<RequisitionLineInput> lines, CancellationToken ct = default);
    Task<Result> SubmitAsync(Guid id, CancellationToken ct = default);
    /// <summary>Aprova a etapa atual (nível 1 se enviada; nível 2 se já no nível 1), conforme o aprovador.</summary>
    Task<Result> ApproveAsync(Guid id, CancellationToken ct = default);
    Task<Result> RejectAsync(Guid id, string? note, CancellationToken ct = default);
    Task<Result<RequisitionView>> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<RequisitionView>> ListAsync(int limit = 200, CancellationToken ct = default);
    /// <summary>Central de Aprovação: pedidos aguardando a decisão do usuário corrente (etapa atual + escopo).</summary>
    Task<IReadOnlyList<RequisitionView>> ListMyApprovalsAsync(CancellationToken ct = default);
    /// <summary>Roteamento v2: marca o pedido APROVADO como atendido pelo estoque interno (baixa já feita).</summary>
    Task<Result> MarkFulfilledFromStockAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tempo de ciclo de aprovação na janela (dias). Respeita o escopo por centro (v2).</summary>
    Task<CycleStatsView> CycleStatsAsync(int days = 90, CancellationToken ct = default);
}
