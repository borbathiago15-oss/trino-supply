using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.Procurement.Application;
using TrinoSupply.Procurement.Domain;
using TrinoSupply.Procurement.Infrastructure.Persistence;

namespace TrinoSupply.Procurement.Infrastructure;

/// <summary>Cadastro de fornecedores (PR-001 / PRC-005).</summary>
public sealed class SupplierService(ProcurementDbContext db, ITenantContext tenant, IUsageMetrics metrics) : ISupplierService
{
    public async Task<Result<Guid>> CreateAsync(string code, string name, string taxId, CancellationToken ct = default)
    {
        if (!tenant.HasTenant)
            return Result.Failure<Guid>(new Error("purchases.no_tenant", "Requisição sem tenant."));

        var result = Supplier.Create(tenant.CompanyId, code, name, taxId);
        if (result.IsFailure) return Result.Failure<Guid>(result.Error);

        db.Suppliers.Add(result.Value);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Result.Failure<Guid>(new Error("purchases.supplier.duplicate", "Já existe fornecedor com este código."));
        }

        metrics.Record("purchases.supplier.created", tenant.CompanyId.Value.ToString());
        return Result.Success(result.Value.Id.Value);
    }

    public async Task<IReadOnlyList<SupplierView>> ListAsync(CancellationToken ct = default)
    {
        var suppliers = await db.Suppliers.AsNoTracking().OrderBy(s => s.Code).ToListAsync(ct);
        return suppliers.Select(s => new SupplierView(s.Id.Value, s.Code, s.Name, s.TaxId, s.Status.ToString())).ToList();
    }
}
