using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.Procurement.Application;
using TrinoSupply.Procurement.Domain;
using TrinoSupply.Procurement.Infrastructure.Persistence;

namespace TrinoSupply.Procurement.Infrastructure;

/// <summary>Cadastro de fornecedores (PR-001 / PRC-005), com os campos fiscais usados na OC.</summary>
public sealed class SupplierService(ProcurementDbContext db, ITenantContext tenant, IUsageMetrics metrics) : ISupplierService
{
    public async Task<Result<Guid>> CreateAsync(SupplierInput input, CancellationToken ct = default)
    {
        if (!tenant.HasTenant)
            return Result.Failure<Guid>(new Error("purchases.no_tenant", "Requisição sem tenant."));

        var result = Supplier.Create(
            tenant.CompanyId, input.Code, input.Name, input.TaxId, input.StateRegistration, input.Address,
            input.District, input.City, input.State, input.ZipCode, input.Phone, input.Email,
            input.PaymentTerms, input.PaymentMethod);
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

    public async Task<Result> UpdateAsync(Guid id, SupplierInput input, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == SupplierId.From(id), ct);
        if (supplier is null)
            return Result.Failure(new Error("purchases.supplier.not_found", "Fornecedor não encontrado."));

        var result = supplier.Update(
            input.Name, input.TaxId, input.StateRegistration, input.Address, input.District, input.City,
            input.State, input.ZipCode, input.Phone, input.Email, input.PaymentTerms, input.PaymentMethod);
        if (result.IsFailure) return result;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(new Error("purchases.conflict", "O fornecedor foi alterado concorrentemente. Recarregue."));
        }

        metrics.Record("purchases.supplier.updated", tenant.CompanyId.Value.ToString());
        return Result.Success();
    }

    public async Task<Result<SupplierView>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var s = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == SupplierId.From(id), ct);
        return s is null
            ? Result.Failure<SupplierView>(new Error("purchases.supplier.not_found", "Fornecedor não encontrado."))
            : Result.Success(ToView(s));
    }

    public async Task<IReadOnlyList<SupplierView>> ListAsync(CancellationToken ct = default)
    {
        var suppliers = await db.Suppliers.AsNoTracking().OrderBy(s => s.Code).ToListAsync(ct);
        return suppliers.Select(ToView).ToList();
    }

    public async Task<IReadOnlyList<SupplierStatsView>> ListStatsAsync(CancellationToken ct = default)
    {
        // RLS já restringe ambos ao tenant corrente; junta a projeção ao cadastro pelo id do fornecedor.
        var stats = await db.SupplierStats.AsNoTracking().ToListAsync(ct);
        if (stats.Count == 0) return Array.Empty<SupplierStatsView>();

        var suppliers = (await db.Suppliers.AsNoTracking().ToListAsync(ct)).ToDictionary(s => s.Id.Value);
        return stats
            .Select(st =>
            {
                suppliers.TryGetValue(st.SupplierId, out var sup);
                return new SupplierStatsView(st.SupplierId, sup?.Code ?? "—", sup?.Name ?? "—",
                    st.OrdersCount, st.TotalValue, st.LastOrderAt);
            })
            .OrderByDescending(s => s.TotalValue)
            .ToList();
    }

    private static SupplierView ToView(Supplier s) => new(
        s.Id.Value, s.Code, s.Name, s.TaxId, s.StateRegistration, s.Address, s.District, s.City, s.State,
        s.ZipCode, s.Phone, s.Email, s.PaymentTerms, s.PaymentMethod, s.Status.ToString());
}
