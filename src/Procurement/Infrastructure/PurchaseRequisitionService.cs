using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.BuildingBlocks.Security;
using TrinoSupply.Procurement.Application;
using TrinoSupply.Procurement.Domain;
using TrinoSupply.Procurement.Infrastructure.Persistence;

namespace TrinoSupply.Procurement.Infrastructure;

/// <summary>Requisição de compra (PR-001): fluxo, SoD e decisão com concorrência otimista.</summary>
public sealed class PurchaseRequisitionService(
    ProcurementDbContext db, ITenantContext tenant, ICurrentUser currentUser, IUsageMetrics metrics, IClock clock)
    : IPurchaseRequisitionService
{
    private static readonly Error Conflict = new("purchases.conflict",
        "A requisição foi alterada concorrentemente. Recarregue e tente novamente.");

    public async Task<Result<Guid>> CreateAsync(IReadOnlyList<RequisitionLineInput> lines, CancellationToken ct = default)
    {
        if (!tenant.HasTenant || string.IsNullOrWhiteSpace(currentUser.Subject))
            return Result.Failure<Guid>(new Error("purchases.no_context", "Requisição sem tenant/usuário."));

        var result = PurchaseRequisition.Create(
            tenant.CompanyId, currentUser.Subject!,
            lines.Select(l => (l.ItemCode, l.Quantity, l.Unit)), clock.UtcNow);
        if (result.IsFailure) return Result.Failure<Guid>(result.Error);

        db.Requisitions.Add(result.Value);
        await db.SaveChangesAsync(ct);
        metrics.Record("purchases.requisition.created", tenant.CompanyId.Value.ToString());
        return Result.Success(result.Value.Id.Value);
    }

    public Task<Result> SubmitAsync(Guid id, CancellationToken ct = default) =>
        MutateAsync(id, r => r.Submit(), "purchases.requisition.submitted", ct);

    public Task<Result> ApproveAsync(Guid id, CancellationToken ct = default) =>
        MutateAsync(id, r => r.Approve(currentUser.Subject ?? string.Empty, clock.UtcNow),
            "purchases.requisition.approved", ct);

    public Task<Result> RejectAsync(Guid id, string? note, CancellationToken ct = default) =>
        MutateAsync(id, r => r.Reject(currentUser.Subject ?? string.Empty, note, clock.UtcNow),
            "purchases.requisition.rejected", ct);

    private async Task<Result> MutateAsync(Guid id, Func<PurchaseRequisition, Result> action, string metric, CancellationToken ct)
    {
        var req = await db.Requisitions.FirstOrDefaultAsync(r => r.Id == RequisitionId.From(id), ct);
        if (req is null)
            return Result.Failure(new Error("purchases.not_found", "Requisição não encontrada."));

        var result = action(req);
        if (result.IsFailure) return result;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(Conflict);
        }

        metrics.Record(metric, req.CompanyId.Value.ToString());
        return Result.Success();
    }

    public async Task<Result<RequisitionView>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var req = await db.Requisitions.AsNoTracking().Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == RequisitionId.From(id), ct);
        return req is null
            ? Result.Failure<RequisitionView>(new Error("purchases.not_found", "Requisição não encontrada."))
            : Result.Success(ToView(req));
    }

    public async Task<IReadOnlyList<RequisitionView>> ListAsync(CancellationToken ct = default)
    {
        var reqs = await db.Requisitions.AsNoTracking().Include(r => r.Lines)
            .OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        return reqs.Select(ToView).ToList();
    }

    private static RequisitionView ToView(PurchaseRequisition r) => new(
        r.Id.Value, r.RequesterSubject, r.Status.ToString(), r.CreatedAt,
        r.DecidedBySubject, r.DecidedAt, r.DecisionNote,
        r.Lines.Select(l => new RequisitionLineView(l.ItemCode, l.Quantity, l.Unit)).ToList());
}
