using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.Materials.Application;
using TrinoSupply.Materials.Domain;
using TrinoSupply.Materials.Infrastructure.Persistence;

namespace TrinoSupply.Materials.Infrastructure;

/// <summary>Cadastro de colaboradores (spec Almoxarifado). RLS isola por tenant.</summary>
public sealed class CollaboratorService(MaterialsDbContext db, ITenantContext tenant, IUsageMetrics metrics)
    : ICollaboratorService
{
    public async Task<Result<Guid>> CreateAsync(CollaboratorInput input, CancellationToken ct = default)
    {
        if (!tenant.HasTenant)
            return Result.Failure<Guid>(new Error("materials.no_tenant", "Requisição sem tenant."));

        var result = Collaborator.Create(tenant.CompanyId, input.Name, input.Registration,
            input.CostCenterCode, input.CompanyCode, input.AdmissionDate);
        if (result.IsFailure) return Result.Failure<Guid>(result.Error);

        db.Collaborators.Add(result.Value);
        await db.SaveChangesAsync(ct);
        metrics.Record("materials.collaborator.created", tenant.CompanyId.Value.ToString());
        return Result.Success(result.Value.Id.Value);
    }

    public async Task<IReadOnlyList<CollaboratorView>> ListAsync(int limit = 500, CancellationToken ct = default)
    {
        var list = await db.Collaborators.AsNoTracking().OrderBy(x => x.Name).Take(Math.Clamp(limit, 1, 2000)).ToListAsync(ct);
        return list.Select(x => new CollaboratorView(
            x.Id.Value, x.Name, x.Registration, x.CostCenterCode, x.CompanyCode, x.AdmissionDate, x.Status.ToString())).ToList();
    }
}
