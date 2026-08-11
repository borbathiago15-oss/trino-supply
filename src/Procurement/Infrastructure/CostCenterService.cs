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

        // Vínculo v2: o centro pode nascer ligado a um CNPJ interno do grupo (empresa pagadora).
        PayingCompanyId? payingId = null;
        if (!string.IsNullOrWhiteSpace(input.PayingCompanyCode))
        {
            var code = input.PayingCompanyCode.Trim().ToUpperInvariant();
            var paying = await db.PayingCompanies.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code, ct);
            if (paying is null)
                return Result.Failure<Guid>(new Error("purchases.paying_company.not_found",
                    $"CNPJ interno '{input.PayingCompanyCode}' não encontrado."));
            payingId = paying.Id;
        }

        var result = CostCenter.Create(tenant.CompanyId, input.Code, input.Name, payingId);
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
        var paying = (await db.PayingCompanies.AsNoTracking().ToListAsync(ct)).ToDictionary(p => p.Id);
        return list.Select(x =>
        {
            var pc = x.PayingCompanyId is { } pid && paying.TryGetValue(pid, out var p) ? p : null;
            return new CostCenterView(x.Id.Value, x.Code, x.Name, x.Status.ToString(), pc?.Code, pc?.LegalName);
        }).ToList();
    }
}
