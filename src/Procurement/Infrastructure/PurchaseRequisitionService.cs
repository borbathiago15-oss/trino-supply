using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.BuildingBlocks.Security;
using TrinoSupply.Procurement.Application;
using TrinoSupply.Procurement.Domain;
using TrinoSupply.Procurement.Infrastructure.Persistence;

namespace TrinoSupply.Procurement.Infrastructure;

/// <summary>Solicitação de compra (PR-001): cabeçalho, fluxo em 2 níveis, SoD e concorrência otimista.</summary>
public sealed class PurchaseRequisitionService(
    ProcurementDbContext db, ITenantContext tenant, ICurrentUser currentUser, IUsageMetrics metrics, IClock clock)
    : IPurchaseRequisitionService
{
    private static readonly Error Conflict = new("purchases.conflict",
        "A solicitação foi alterada concorrentemente. Recarregue e tente novamente.");

    public async Task<Result<Guid>> CreateAsync(CreateRequisitionInput input, CancellationToken ct = default)
    {
        if (!tenant.HasTenant || string.IsNullOrWhiteSpace(currentUser.Subject))
            return Result.Failure<Guid>(new Error("purchases.no_context", "Requisição sem tenant/usuário."));

        var payCode = (input.PayingCompanyCode ?? string.Empty).Trim().ToUpperInvariant();
        var paying = await db.PayingCompanies.AsNoTracking().FirstOrDefaultAsync(p => p.Code == payCode, ct);
        if (paying is null)
            return Result.Failure<Guid>(new Error("purchases.paying_company.not_found", $"Empresa do custo '{input.PayingCompanyCode}' não encontrada."));

        var ccCode = (input.CostCenterCode ?? string.Empty).Trim().ToUpperInvariant();
        var costCenter = await db.CostCenters.AsNoTracking().FirstOrDefaultAsync(c => c.Code == ccCode, ct);
        if (costCenter is null)
            return Result.Failure<Guid>(new Error("purchases.cost_center.not_found", $"Centro de custo '{input.CostCenterCode}' não encontrado."));

        var priority = string.Equals(input.Priority, "Emergencial", StringComparison.OrdinalIgnoreCase)
            ? RequisitionPriority.Emergencial : RequisitionPriority.Normal;

        var result = PurchaseRequisition.Create(
            tenant.CompanyId, currentUser.Subject!, paying.Id, costCenter.Id, priority, input.Justification ?? string.Empty,
            input.ApproverLevel1Subject ?? string.Empty, input.ApproverLevel2Subject ?? string.Empty,
            (input.Lines ?? Array.Empty<RequisitionLineInput>()).Select(l => (l.ItemCode, l.Quantity, l.Unit)), clock.UtcNow);
        if (result.IsFailure) return Result.Failure<Guid>(result.Error);

        db.Requisitions.Add(result.Value);
        await db.SaveChangesAsync(ct);
        metrics.Record("purchases.requisition.created", tenant.CompanyId.Value.ToString());
        return Result.Success(result.Value.Id.Value);
    }

    public Task<Result> AddLinesAsync(Guid id, IReadOnlyList<RequisitionLineInput> lines, CancellationToken ct = default) =>
        MutateAsync(id, r => r.AddLines(lines.Select(l => (l.ItemCode, l.Quantity, l.Unit))),
            "purchases.requisition.lines_added", ct);

    public Task<Result> SubmitAsync(Guid id, CancellationToken ct = default) =>
        MutateAsync(id, r => r.Submit(), "purchases.requisition.submitted", ct);

    public Task<Result> ApproveAsync(Guid id, CancellationToken ct = default) =>
        MutateAsync(id, r =>
        {
            var subject = currentUser.Subject ?? string.Empty;
            return r.Status switch
            {
                RequisitionStatus.Submitted => r.ApproveLevel1(subject, clock.UtcNow),
                RequisitionStatus.ApprovedLevel1 => r.ApproveLevel2(subject, clock.UtcNow),
                _ => Result.Failure(new Error("purchases.not_submitted", "Solicitação não está em etapa de aprovação."))
            };
        }, "purchases.requisition.approved", ct);

    public Task<Result> RejectAsync(Guid id, string? note, CancellationToken ct = default) =>
        MutateAsync(id, r => r.Reject(currentUser.Subject ?? string.Empty, note, clock.UtcNow),
            "purchases.requisition.rejected", ct);

    private async Task<Result> MutateAsync(Guid id, Func<PurchaseRequisition, Result> action, string metric, CancellationToken ct)
    {
        var req = await db.Requisitions.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == RequisitionId.From(id), ct);
        if (req is null)
            return Result.Failure(new Error("purchases.not_found", "Solicitação não encontrada."));

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
        if (req is null)
            return Result.Failure<RequisitionView>(new Error("purchases.not_found", "Solicitação não encontrada."));

        var paying = await db.PayingCompanies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.PayingCompanyId, ct);
        var cc = await db.CostCenters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CostCenterId, ct);
        return Result.Success(ToView(req, paying, cc));
    }

    public async Task<IReadOnlyList<RequisitionView>> ListAsync(CancellationToken ct = default)
    {
        var reqs = await db.Requisitions.AsNoTracking().Include(r => r.Lines)
            .OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        var paying = (await db.PayingCompanies.AsNoTracking().ToListAsync(ct)).ToDictionary(p => p.Id.Value);
        var centers = (await db.CostCenters.AsNoTracking().ToListAsync(ct)).ToDictionary(c => c.Id.Value);
        return reqs.Select(r => ToView(r,
            paying.GetValueOrDefault(r.PayingCompanyId.Value),
            centers.GetValueOrDefault(r.CostCenterId.Value))).ToList();
    }

    private static RequisitionView ToView(PurchaseRequisition r, PayingCompany? paying, CostCenter? cc) => new(
        r.Id.Value, r.RequesterSubject, r.Status.ToString(), r.CreatedAt,
        paying?.Code ?? string.Empty, paying?.LegalName ?? string.Empty,
        cc?.Code ?? string.Empty, cc?.Name ?? string.Empty,
        r.Priority.ToString(), r.Justification, r.ApproverLevel1Subject, r.ApproverLevel2Subject,
        r.Level1DecidedBySubject, r.Level2DecidedBySubject, r.RejectedBySubject, r.DecisionNote,
        r.Lines.Select(l => new RequisitionLineView(l.ItemCode, l.Quantity, l.Unit)).ToList());
}
