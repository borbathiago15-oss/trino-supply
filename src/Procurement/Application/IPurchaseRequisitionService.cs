using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Procurement.Application;

public sealed record RequisitionLineInput(string ItemCode, decimal Quantity, string Unit);
public sealed record RequisitionLineView(string ItemCode, decimal Quantity, string Unit);
public sealed record RequisitionView(
    Guid Id, string Requester, string Status, DateTimeOffset CreatedAt,
    string? DecidedBy, DateTimeOffset? DecidedAt, string? DecisionNote,
    IReadOnlyList<RequisitionLineView> Lines);

/// <summary>
/// Casos de uso de requisição de compra (PR-001). Requisitante e aprovador vêm do JWT (subject);
/// a Segregation of Duties é aplicada no domínio (o requisitante não aprova a própria requisição).
/// </summary>
public interface IPurchaseRequisitionService
{
    Task<Result<Guid>> CreateAsync(IReadOnlyList<RequisitionLineInput> lines, CancellationToken ct = default);
    /// <summary>Acrescenta itens a um rascunho (item manual na tela ou importação em lote via planilha).</summary>
    Task<Result> AddLinesAsync(Guid id, IReadOnlyList<RequisitionLineInput> lines, CancellationToken ct = default);
    Task<Result> SubmitAsync(Guid id, CancellationToken ct = default);
    Task<Result> ApproveAsync(Guid id, CancellationToken ct = default);
    Task<Result> RejectAsync(Guid id, string? note, CancellationToken ct = default);
    Task<Result<RequisitionView>> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<RequisitionView>> ListAsync(CancellationToken ct = default);
}
