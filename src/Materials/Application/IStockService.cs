using TrinoSupply.BuildingBlocks;
using TrinoSupply.Materials.Domain;

namespace TrinoSupply.Materials.Application;

public sealed record BalanceView(Guid ItemId, string ItemCode, decimal Quantity, int Version);
public sealed record MovementView(Guid Id, string Direction, decimal Quantity, DateTimeOffset OccurredAt, string? Reason);

/// <summary>
/// Estoque (MMS-002): registra movimentos no ledger e mantém o saldo (projeção). O lançamento é
/// serializado por chave de saldo — entradas/saídas simultâneas do mesmo item são consistentes.
/// </summary>
public interface IStockService
{
    /// <summary>Lança um movimento e retorna o novo saldo do item.</summary>
    Task<Result<decimal>> PostMovementAsync(
        string itemCode, StockDirection direction, decimal quantity, string? reason, CancellationToken ct = default);

    Task<Result<BalanceView>> GetBalanceAsync(string itemCode, CancellationToken ct = default);
    Task<IReadOnlyList<MovementView>> ListMovementsAsync(string itemCode, CancellationToken ct = default);
}
