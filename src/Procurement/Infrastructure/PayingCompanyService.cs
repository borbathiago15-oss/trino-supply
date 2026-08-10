using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.Procurement.Application;
using TrinoSupply.Procurement.Domain;
using TrinoSupply.Procurement.Infrastructure.Persistence;

namespace TrinoSupply.Procurement.Infrastructure;

/// <summary>Cadastro de empresas pagadoras (múltiplos CNPJs do grupo). Base do cabeçalho comprador da OC.</summary>
public sealed class PayingCompanyService(ProcurementDbContext db, ITenantContext tenant, IUsageMetrics metrics)
    : IPayingCompanyService
{
    public async Task<Result<Guid>> CreateAsync(PayingCompanyInput input, CancellationToken ct = default)
    {
        if (!tenant.HasTenant)
            return Result.Failure<Guid>(new Error("purchases.no_tenant", "Requisição sem tenant."));

        var result = PayingCompany.Create(
            tenant.CompanyId, input.Code, input.LegalName, input.TaxId, input.StateRegistration, input.Address,
            input.District, input.City, input.State, input.ZipCode, input.Phone, input.Email);
        if (result.IsFailure) return Result.Failure<Guid>(result.Error);

        db.PayingCompanies.Add(result.Value);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Result.Failure<Guid>(new Error("purchases.paying_company.duplicate", "Já existe empresa pagadora com este código."));
        }

        metrics.Record("purchases.paying_company.created", tenant.CompanyId.Value.ToString());
        return Result.Success(result.Value.Id.Value);
    }

    public async Task<Result> UpdateAsync(Guid id, PayingCompanyInput input, CancellationToken ct = default)
    {
        var pc = await db.PayingCompanies.FirstOrDefaultAsync(x => x.Id == PayingCompanyId.From(id), ct);
        if (pc is null)
            return Result.Failure(new Error("purchases.paying_company.not_found", "Empresa pagadora não encontrada."));

        var result = pc.Update(
            input.LegalName, input.TaxId, input.StateRegistration, input.Address, input.District, input.City,
            input.State, input.ZipCode, input.Phone, input.Email);
        if (result.IsFailure) return result;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(new Error("purchases.conflict", "A empresa pagadora foi alterada concorrentemente. Recarregue."));
        }

        metrics.Record("purchases.paying_company.updated", tenant.CompanyId.Value.ToString());
        return Result.Success();
    }

    public async Task<Result<PayingCompanyView>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var pc = await db.PayingCompanies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == PayingCompanyId.From(id), ct);
        return pc is null
            ? Result.Failure<PayingCompanyView>(new Error("purchases.paying_company.not_found", "Empresa pagadora não encontrada."))
            : Result.Success(ToView(pc));
    }

    public async Task<IReadOnlyList<PayingCompanyView>> ListAsync(CancellationToken ct = default)
    {
        var list = await db.PayingCompanies.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct);
        return list.Select(ToView).ToList();
    }

    private static PayingCompanyView ToView(PayingCompany p) => new(
        p.Id.Value, p.Code, p.LegalName, p.TaxId, p.StateRegistration, p.Address, p.District, p.City,
        p.State, p.ZipCode, p.Phone, p.Email, p.Status.ToString());
}
