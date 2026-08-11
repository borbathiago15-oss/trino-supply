using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.Materials.Application;
using TrinoSupply.Materials.Domain;
using TrinoSupply.Materials.Infrastructure.Persistence;

namespace TrinoSupply.Materials.Infrastructure;

/// <summary>Estoque (MMS-002): ledger + saldo (projeção), com serialização por chave de saldo.</summary>
public sealed class StockService(MaterialsDbContext db, ITenantContext tenant, IUsageMetrics metrics, IClock clock,
    TrinoSupply.Foundation.Infrastructure.Audit.IBusinessAudit audit)
    : IStockService
{
    public async Task<Result<decimal>> PostMovementAsync(
        string itemCode, StockDirection direction, decimal quantity, string? reason, CancellationToken ct = default)
    {
        if (!tenant.HasTenant)
            return Result.Failure<decimal>(new Error("materials.no_tenant", "Requisição sem tenant."));
        if (quantity <= 0)
            return Result.Failure<decimal>(new Error("materials.stock.qty_invalid", "Quantidade deve ser positiva."));

        var item = await db.Items.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Code == itemCode.Trim().ToUpperInvariant(), ct);
        if (item is null)
            return Result.Failure<decimal>(new Error("materials.item.not_found", $"Item '{itemCode}' não encontrado."));

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Serialização por chave de saldo (ARC-006 §12): trava a linha do saldo. Lançamentos
        // simultâneos do MESMO item enfileiram aqui — sem perda de atualização. RLS aplica-se ao lock.
        await db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM materials.stock_balance WHERE item_id = {0} FOR UPDATE", item.Id.Value);

        var balance = await db.StockBalances.FirstOrDefaultAsync(bx => bx.Id == item.Id, ct);
        if (balance is null)
            return Result.Failure<decimal>(new Error("materials.stock.no_balance", "Saldo do item inexistente."));

        var delta = direction == StockDirection.In ? quantity : -quantity;
        var applied = balance.Apply(delta);
        if (applied.IsFailure)
            return Result.Failure<decimal>(applied.Error);

        db.StockMovements.Add(StockMovement.Create(
            tenant.CompanyId, item.Id, direction, quantity, clock.UtcNow, reason));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        metrics.Record("materials.movement.posted", tenant.CompanyId.Value.ToString());
        await audit.RecordAsync("materials.movement.posted", "Item", item.Code,
            new { direction = direction.ToString(), quantity, balance = balance.Quantity, reason }, ct);
        return Result.Success(balance.Quantity);
    }

    public async Task<Result<BalanceView>> GetBalanceAsync(string itemCode, CancellationToken ct = default)
    {
        var item = await db.Items.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Code == itemCode.Trim().ToUpperInvariant(), ct);
        if (item is null)
            return Result.Failure<BalanceView>(new Error("materials.item.not_found", $"Item '{itemCode}' não encontrado."));

        var balance = await db.StockBalances.AsNoTracking().FirstOrDefaultAsync(bx => bx.Id == item.Id, ct);
        var qty = balance?.Quantity ?? 0m;
        var version = balance?.Version ?? 0;
        return Result.Success(new BalanceView(item.Id.Value, item.Code, qty, version));
    }

    public async Task<IReadOnlyList<MovementView>> ListMovementsAsync(string itemCode, CancellationToken ct = default)
    {
        var normalized = itemCode.Trim().ToUpperInvariant();
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Code == normalized, ct);
        if (item is null) return [];

        var movements = await db.StockMovements.AsNoTracking()
            .Where(mv => mv.ItemId == item.Id)
            .OrderByDescending(mv => mv.OccurredAt)
            .ToListAsync(ct);
        return movements.Select(mv => new MovementView(
            mv.Id, mv.Direction.ToString(), mv.Quantity, mv.OccurredAt, mv.Reason)).ToList();
    }
}
