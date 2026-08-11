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
/// Solicitação de almoxarifado (Fluxo A). Máquina de estados no domínio; aqui ficam a resolução do
/// usuário corrente, a checagem de estoque na separação e a baixa no estoque na entrega (atômica).
/// </summary>
public sealed class StockRequestService(
    MaterialsDbContext db, ITenantContext tenant, ICurrentUser currentUser, IUsageMetrics metrics, IClock clock)
    : IStockRequestService
{
    public async Task<Result<Guid>> CreateAsync(CreateStockRequestInput input, CancellationToken ct = default)
    {
        if (!tenant.HasTenant || string.IsNullOrWhiteSpace(currentUser.Subject))
            return Result.Failure<Guid>(new Error("warehouse.no_tenant", "Requisição sem tenant/usuário."));

        var created = StockRequest.Create(tenant.CompanyId, currentUser.Subject!, input.CompanyCode,
            input.CostCenterCode, input.ManagerSubject, input.Reason,
            input.Lines.Select(l => (l.ItemCode, l.Quantity)), clock.UtcNow);
        if (created.IsFailure) return Result.Failure<Guid>(created.Error);

        db.StockRequests.Add(created.Value);
        await db.SaveChangesAsync(ct);
        metrics.Record("warehouse.request.created", tenant.CompanyId.Value.ToString());
        return Result.Success(created.Value.Id.Value);
    }

    public async Task<IReadOnlyList<StockRequestView>> ListAsync(bool all, CancellationToken ct = default)
    {
        var subject = currentUser.Subject ?? string.Empty;
        var query = db.StockRequests.AsNoTracking().Include(x => x.Lines).AsQueryable();
        if (!all)
            query = query.Where(x => x.RequesterSubject == subject || x.ManagerSubject == subject);

        var list = await query.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        if (list.Count == 0) return [];

        // Saldo atual por item (para a visão do almoxarifado decidir separação/compra).
        var codes = list.SelectMany(r => r.Lines).Select(l => l.ItemCode).ToHashSet();
        var items = await db.Items.AsNoTracking().Where(i => codes.Contains(i.Code)).ToListAsync(ct);
        var itemIds = items.ToDictionary(i => i.Code, i => i.Id);
        var ids = itemIds.Values.ToList();
        var balances = await db.StockBalances.AsNoTracking()
            .Where(b => ids.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Quantity, ct);
        decimal Bal(string code) => itemIds.TryGetValue(code, out var id) && balances.TryGetValue(id, out var q) ? q : 0m;

        return list.Select(r => new StockRequestView(
            r.Id.Value, r.RequesterSubject, r.CompanyCode, r.CostCenterCode, r.ManagerSubject, r.Reason,
            r.Status.ToString(), r.CreatedAt, r.DecisionBySubject, r.DecisionAt, r.DecisionNote,
            r.Lines.Select(l => new StockRequestLineView(l.ItemCode, l.Quantity, Bal(l.ItemCode))).ToList())).ToList();
    }

    public Task<Result> ApproveAsync(Guid id, CancellationToken ct = default) =>
        MutateAsync(id, r => r.Approve(currentUser.Subject ?? string.Empty, clock.UtcNow), ct);

    public Task<Result> RejectAsync(Guid id, string? note, CancellationToken ct = default) =>
        MutateAsync(id, r => r.Reject(currentUser.Subject ?? string.Empty, note, clock.UtcNow), ct);

    public Task<Result> DispatchAsync(Guid id, CancellationToken ct = default) =>
        MutateAsync(id, r => r.Dispatch(clock.UtcNow), ct);

    public Task<Result> CancelAsync(Guid id, string? note, CancellationToken ct = default) =>
        MutateAsync(id, r => r.Cancel(currentUser.Subject ?? string.Empty, note, clock.UtcNow), ct);

    public async Task<Result> StartSeparationAsync(Guid id, CancellationToken ct = default)
    {
        var req = await db.StockRequests.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == StockRequestId.From(id), ct);
        if (req is null) return Result.Failure(new Error("warehouse.not_found", "Solicitação não encontrada."));

        // Há estoque para TODAS as linhas? (agrega por item, pois o mesmo item pode repetir)
        var needed = req.Lines.GroupBy(l => l.ItemCode).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var neededCodes = needed.Keys.ToList();
        var items = await db.Items.AsNoTracking().Where(i => neededCodes.Contains(i.Code)).ToListAsync(ct);
        var itemIds = items.ToDictionary(i => i.Code, i => i.Id);
        var ids = itemIds.Values.ToList();
        var balances = await db.StockBalances.AsNoTracking()
            .Where(b => ids.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Quantity, ct);

        var inStock = needed.All(n => itemIds.TryGetValue(n.Key, out var iid)
            && balances.TryGetValue(iid, out var q) && q >= n.Value);

        var r = req.StartSeparation(inStock, clock.UtcNow);
        if (r.IsFailure) return r;
        await db.SaveChangesAsync(ct);
        metrics.Record(inStock ? "warehouse.request.separating" : "warehouse.request.purchase_needed",
            tenant.CompanyId.Value.ToString());
        return Result.Success();
    }

    public async Task<Result> DeliverAsync(Guid id, CancellationToken ct = default)
    {
        var req = await db.StockRequests.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == StockRequestId.From(id), ct);
        if (req is null) return Result.Failure(new Error("warehouse.not_found", "Solicitação não encontrada."));

        var mark = req.Deliver(partial: false, clock.UtcNow);
        if (mark.IsFailure) return mark;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var line in req.Lines)
        {
            var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Code == line.ItemCode, ct);
            if (item is null)
                return Result.Failure(new Error("materials.item.not_found", $"Item '{line.ItemCode}' não encontrado."));

            await db.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM materials.stock_balance WHERE item_id = {0} FOR UPDATE", item.Id.Value);

            var balance = await db.StockBalances.FirstOrDefaultAsync(bx => bx.Id == item.Id, ct);
            if (balance is null) return Result.Failure(new Error("materials.stock.no_balance", "Saldo do item inexistente."));

            var applied = balance.Apply(-line.Quantity);
            if (applied.IsFailure) return Result.Failure(applied.Error);

            db.StockMovements.Add(StockMovement.Create(
                tenant.CompanyId, item.Id, StockDirection.Out, line.Quantity, clock.UtcNow,
                $"Entrega almoxarifado — solicitação {req.Id}"));
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        metrics.Record("warehouse.request.delivered", tenant.CompanyId.Value.ToString());
        return Result.Success();
    }

    private async Task<Result> MutateAsync(Guid id, Func<StockRequest, Result> action, CancellationToken ct)
    {
        var req = await db.StockRequests.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == StockRequestId.From(id), ct);
        if (req is null) return Result.Failure(new Error("warehouse.not_found", "Solicitação não encontrada."));

        var r = action(req);
        if (r.IsFailure) return r;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
