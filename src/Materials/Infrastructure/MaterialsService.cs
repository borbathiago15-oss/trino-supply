using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.Materials.Application;
using TrinoSupply.Materials.Domain;
using TrinoSupply.Materials.Infrastructure.Persistence;

namespace TrinoSupply.Materials.Infrastructure;

/// <summary>Implementação dos casos de uso de Materiais (MMS-002) sobre o <see cref="MaterialsDbContext"/>.</summary>
public sealed class MaterialsService(MaterialsDbContext db, ITenantContext tenant, IUsageMetrics metrics) : IMaterialsService
{
    public async Task<Result<Guid>> CreateUnitAsync(
        string code, string name, string dimension, decimal factorToBase, CancellationToken ct = default)
    {
        if (!tenant.HasTenant)
            return Result.Failure<Guid>(new Error("materials.no_tenant", "Requisição sem tenant."));

        var result = UnitOfMeasure.Create(tenant.CompanyId, code, name, dimension, factorToBase);
        if (result.IsFailure) return Result.Failure<Guid>(result.Error);

        db.Units.Add(result.Value);
        await db.SaveChangesAsync(ct);
        metrics.Record("materials.unit.created", tenant.CompanyId.Value.ToString());
        return Result.Success(result.Value.Id.Value);
    }

    public async Task<IReadOnlyList<UnitView>> ListUnitsAsync(CancellationToken ct = default)
    {
        var units = await db.Units.AsNoTracking().OrderBy(u => u.Code).ToListAsync(ct);
        return units.Select(u => new UnitView(u.Id.Value, u.Code, u.Name, u.Dimension, u.FactorToBase)).ToList();
    }

    public async Task<Result<Guid>> CreateItemAsync(
        string code, string name, string baseUnitCode, CancellationToken ct = default)
    {
        if (!tenant.HasTenant)
            return Result.Failure<Guid>(new Error("materials.no_tenant", "Requisição sem tenant."));

        var unit = await FindUnitAsync(baseUnitCode, ct);
        if (unit is null)
            return Result.Failure<Guid>(new Error("materials.unit.not_found", $"Unidade '{baseUnitCode}' não encontrada."));

        var result = Item.Create(tenant.CompanyId, code, name, unit.Id);
        if (result.IsFailure) return Result.Failure<Guid>(result.Error);

        db.Items.Add(result.Value);
        await db.SaveChangesAsync(ct);
        metrics.Record("materials.item.created", tenant.CompanyId.Value.ToString());
        return Result.Success(result.Value.Id.Value);
    }

    public async Task<IReadOnlyList<ItemView>> ListItemsAsync(CancellationToken ct = default)
    {
        var items = await db.Items.AsNoTracking().OrderBy(i => i.Code).ToListAsync(ct);
        return items.Select(i => new ItemView(
            i.Id.Value, i.Code, i.Name, i.BaseUnitId.Value, i.Status.ToString())).ToList();
    }

    public async Task<Result<decimal>> ConvertAsync(
        decimal quantity, string fromUnitCode, string toUnitCode, CancellationToken ct = default)
    {
        var from = await FindUnitAsync(fromUnitCode, ct);
        var to = await FindUnitAsync(toUnitCode, ct);
        if (from is null || to is null)
            return Result.Failure<decimal>(new Error("materials.unit.not_found", "Unidade de origem/destino não encontrada."));

        return from.ConvertTo(quantity, to);
    }

    private Task<UnitOfMeasure?> FindUnitAsync(string code, CancellationToken ct)
    {
        var normalized = code.Trim().ToLowerInvariant();
        return db.Units.FirstOrDefaultAsync(u => u.Code == normalized, ct);
    }
}
