using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.Procurement.Application;
using TrinoSupply.Procurement.Domain;
using TrinoSupply.Procurement.Infrastructure.Persistence;

namespace TrinoSupply.Procurement.Infrastructure;

/// <summary>Cadastro de centros de custo (spec Sistema de Compras). Tenant-scoped (RLS), código único.</summary>
public sealed class CostCenterService(ProcurementDbContext db, ITenantContext tenant, IUsageMetrics metrics)
    : ICostCenterService
{
    public async Task<Result<Guid>> CreateAsync(CostCenterInput input, CancellationToken ct = default)
    {
        if (!tenant.HasTenant)
            return Result.Failure<Guid>(new Error("purchases.no_tenant", "Requisição sem tenant."));

        var result = CostCenter.Create(tenant.CompanyId, input.Code, input.Name);
        if (result.IsFailure) return Result.Failure<Guid>(result.Error);

        db.CostCenters.Add(result.Value);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Result.Failure<Guid>(new Error("purchases.cost_center.duplicate", "Já existe centro de custo com este código."));
        }

        metrics.Record("purchases.cost_center.created", tenant.CompanyId.Value.ToString());
        return Result.Success(result.Value.Id.Value);
    }

    public async Task<IReadOnlyList<CostCenterView>> ListAsync(CancellationToken ct = default)
    {
        var list = await db.CostCenters.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct);
        return list.Select(x => new CostCenterView(x.Id.Value, x.Code, x.Name, x.Status.ToString())).ToList();
    }
}
