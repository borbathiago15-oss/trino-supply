using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.BuildingBlocks.Security;
using TrinoSupply.Materials.Application;
using TrinoSupply.Materials.Domain;
using TrinoSupply.Materials.Infrastructure.Persistence;

namespace TrinoSupply.Materials.Infrastructure;

/// <summary>
/// Baixa de consumo (spec Almoxarifado — entrega ao colaborador). Registra a entrega e dá saída no
/// estoque: cada linha trava a chave de saldo (FOR UPDATE) e aplica a saída dentro de UMA transação —
/// se faltar saldo em qualquer item, a baixa inteira é revertida (atômica).
/// </summary>
public sealed class ConsumptionService(
    MaterialsDbContext db, ITenantContext tenant, ICurrentUser currentUser, IUsageMetrics metrics, IClock clock,
    TrinoSupply.Foundation.Infrastructure.Audit.IBusinessAudit audit)
    : IConsumptionService
{
    public async Task<Result<Guid>> CreateAsync(CreateConsumptionInput input, CancellationToken ct = default)
    {
        if (!tenant.HasTenant || string.IsNullOrWhiteSpace(currentUser.Subject))
            return Result.Failure<Guid>(new Error("materials.no_tenant", "Requisição sem tenant/usuário."));

        var collaborator = await db.Collaborators.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == CollaboratorId.From(input.CollaboratorId), ct);
        if (collaborator is null)
            return Result.Failure<Guid>(new Error("materials.collaborator.not_found", "Colaborador não encontrado."));

        var created = Consumption.Create(
            tenant.CompanyId, input.CompanyCode, input.CostCenterCode, CollaboratorId.From(input.CollaboratorId),
            input.Reason, currentUser.Subject!, input.Lines.Select(l => (l.ItemCode, l.Quantity)), clock.UtcNow);
        if (created.IsFailure) return Result.Failure<Guid>(created.Error);
        var consumption = created.Value;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var line in consumption.Lines)
        {
            var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Code == line.ItemCode, ct);
            if (item is null)
                return Result.Failure<Guid>(new Error("materials.item.not_found", $"Item '{line.ItemCode}' não encontrado."));

            // Serialização por chave de saldo (ARC-006 §12): trava a linha antes de aplicar a saída.
            await db.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM materials.stock_balance WHERE item_id = {0} FOR UPDATE", item.Id.Value);

            var balance = await db.StockBalances.FirstOrDefaultAsync(bx => bx.Id == item.Id, ct);
            if (balance is null)
                return Result.Failure<Guid>(new Error("materials.stock.no_balance", "Saldo do item inexistente."));

            var applied = balance.Apply(-line.Quantity);
            if (applied.IsFailure) return Result.Failure<Guid>(applied.Error);

            db.StockMovements.Add(StockMovement.Create(
                tenant.CompanyId, item.Id, StockDirection.Out, line.Quantity, clock.UtcNow,
                $"Consumo: {consumption.Reason} — colaborador {collaborator.Name}"));
        }

        db.Consumptions.Add(consumption);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        metrics.Record("materials.consumption.posted", tenant.CompanyId.Value.ToString());
        await audit.RecordAsync("materials.consumption.posted", "Consumption", consumption.Id.Value.ToString(),
            new { collaborator = collaborator.Name, costCenter = consumption.CostCenterCode, reason = consumption.Reason,
                  items = consumption.Lines.Select(l => $"{l.ItemCode}x{l.Quantity}").ToArray() }, ct);
        return Result.Success(consumption.Id.Value);
    }

    public async Task<IReadOnlyList<ConsumptionView>> ListAsync(int limit = 200, CancellationToken ct = default)
    {
        var list = await db.Consumptions.AsNoTracking().Include(x => x.Lines)
            .OrderByDescending(x => x.IssuedAt).Take(Math.Clamp(limit, 1, 1000)).ToListAsync(ct);
        if (list.Count == 0) return [];

        var collaborators = await db.Collaborators.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return list.Select(x => new ConsumptionView(
            x.Id.Value, x.CompanyCode, x.CostCenterCode, x.CollaboratorId.Value,
            collaborators.TryGetValue(x.CollaboratorId, out var n) ? n : "—",
            x.Reason, x.IssuedBySubject, x.IssuedAt,
            x.Lines.Select(l => new ConsumptionLineView(l.ItemCode, l.Quantity)).ToList())).ToList();
    }

    public async Task<Result<ConsumptionFicha>> GetFichaAsync(Guid id, CancellationToken ct = default)
    {
        var c = await db.Consumptions.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == ConsumptionId.From(id), ct);
        if (c is null)
            return Result.Failure<ConsumptionFicha>(new Error("materials.consumption.not_found", "Baixa de consumo não encontrada."));

        var collaborator = await db.Collaborators.AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.CollaboratorId, ct);
        var codes = c.Lines.Select(l => l.ItemCode).ToHashSet();
        var items = (await db.Items.AsNoTracking().Where(i => codes.Contains(i.Code)).ToListAsync(ct))
            .ToDictionary(i => i.Code);

        var lines = c.Lines.Select(l =>
        {
            items.TryGetValue(l.ItemCode, out var item);
            return new FichaLine(item?.Name ?? l.ItemCode, l.ItemCode, item?.Group ?? "—", item?.Ca, l.Quantity, c.IssuedAt);
        }).ToList();

        return Result.Success(new ConsumptionFicha(
            c.CompanyCode, c.CostCenterCode, collaborator?.Name ?? "—", collaborator?.Registration,
            collaborator?.AdmissionDate, c.Reason, c.IssuedBySubject, c.IssuedAt, lines));
    }
}
