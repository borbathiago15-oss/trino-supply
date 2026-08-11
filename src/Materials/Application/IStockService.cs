using TrinoSupply.BuildingBlocks;
using TrinoSupply.Materials.Domain;

namespace TrinoSupply.Materials.Application;

public sealed record BalanceView(Guid ItemId, string ItemCode, decimal Quantity, int Version);
public sealed record MovementView(
    Guid Id, string Direction, decimal Quantity, DateTimeOffset OccurredAt, string? Reason, string? CostCenterCode);

/// <summary>
/// Estoque (MMS-002): registra movimentos no ledger e mantém o saldo (projeção). O lançamento é
/// serializado por chave de saldo — entradas/saídas simultâneas do mesmo item são consistentes.
/// Spec v2: TODA saída informa o centro de custo debitado; entrada é opcional.
/// </summary>
public interface IStockService
{
    /// <summary>Lança um movimento e retorna o novo saldo do item. Saída exige centro de custo.</summary>
    Task<Result<decimal>> PostMovementAsync(
        string itemCode, StockDirection direction, decimal quantity, string? reason,
        string? costCenterCode = null, CancellationToken ct = default);

    /// <summary>
    /// Lote ATÔMICO de entrada OU saída (v2): todas as linhas na mesma transação; item inexistente
    /// ou saldo insuficiente em QUALQUER linha aborta tudo. Saída exige centro de custo.
    /// </summary>
    Task<Result> PostBatchAsync(
        StockDirection direction, IReadOnlyList<(string ItemCode, decimal Quantity)> lines,
        string reason, string? costCenterCode = null, CancellationToken ct = default);

    Task<Result<BalanceView>> GetBalanceAsync(string itemCode, CancellationToken ct = default);
    Task<IReadOnlyList<MovementView>> ListMovementsAsync(string itemCode, int limit = 200, CancellationToken ct = default);
}
