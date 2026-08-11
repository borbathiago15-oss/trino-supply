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

    /// <summary>
    /// Baixa em lote ATÔMICA (v2 — atendimento de pedido pelo estoque interno): todas as linhas saem
    /// em uma transação; item inexistente ou saldo insuficiente em QUALQUER linha aborta tudo.
    /// </summary>
    Task<Result> DebitBatchAsync(
        IReadOnlyList<(string ItemCode, decimal Quantity)> lines, string reason, CancellationToken ct = default);

    Task<Result<BalanceView>> GetBalanceAsync(string itemCode, CancellationToken ct = default);
    Task<IReadOnlyList<MovementView>> ListMovementsAsync(string itemCode, int limit = 200, CancellationToken ct = default);
}
