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
        string code, string name, string baseUnitCode, string? group = null, string? ca = null, CancellationToken ct = default)
    {
        if (!tenant.HasTenant)
            return Result.Failure<Guid>(new Error("materials.no_tenant", "Requisição sem tenant."));

        var unit = await FindUnitAsync(baseUnitCode, ct);
        if (unit is null)
            return Result.Failure<Guid>(new Error("materials.unit.not_found", $"Unidade '{baseUnitCode}' não encontrada."));

        var result = Item.Create(tenant.CompanyId, code, name, unit.Id, group, ca);
        if (result.IsFailure) return Result.Failure<Guid>(result.Error);

        db.Items.Add(result.Value);
        // Saldo criado zerado junto com o item → movimentos são sempre UPDATE (sem corrida de INSERT).
        db.StockBalances.Add(StockBalance.Create(tenant.CompanyId, result.Value.Id));
        await db.SaveChangesAsync(ct);
        metrics.Record("materials.item.created", tenant.CompanyId.Value.ToString());
        return Result.Success(result.Value.Id.Value);
    }

    public async Task<IReadOnlyList<ItemView>> ListItemsAsync(string? group = null, int limit = 500, CancellationToken ct = default)
    {
        var query = db.Items.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(group))
        {
            var g = ProductGroups.Normalize(group);
            query = query.Where(i => i.Group == g);
        }
        var items = await query.OrderBy(i => i.Code).Take(Math.Clamp(limit, 1, 2000)).ToListAsync(ct);
        return items.Select(i => new ItemView(
            i.Id.Value, i.Code, i.Name, i.BaseUnitId.Value, i.Status.ToString(), i.Group, i.Ca)).ToList();
    }

    public async Task<ItemImportResult> ImportItemsAsync(IReadOnlyList<ItemImportRow> rows, CancellationToken ct = default)
    {
        if (!tenant.HasTenant)
            return new ItemImportResult(0, new[] { "Requisição sem tenant." });

        var errors = new List<string>();
        var existing = (await db.Items.AsNoTracking().Select(i => i.Code).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var unitByCode = (await db.Units.AsNoTracking().ToListAsync(ct)).ToDictionary(u => u.Code, u => u.Id);
        var newUnits = new Dictionary<string, UnitId>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var imported = 0;

        for (var idx = 0; idx < rows.Count; idx++)
        {
            var r = rows[idx];
            var line = idx + 2; // linha 1 = cabeçalho
            var code = (r.Code ?? string.Empty).Trim().ToUpperInvariant();
            if (code.Length == 0) { errors.Add($"Linha {line}: código vazio."); continue; }
            if (string.IsNullOrWhiteSpace(r.Name)) { errors.Add($"Linha {line}: descrição vazia."); continue; }
            if (existing.Contains(code) || !seen.Add(code)) { errors.Add($"Linha {line}: código '{code}' duplicado."); continue; }

            var unitCode = (r.BaseUnitCode ?? "un").Trim().ToLowerInvariant();
            if (unitCode.Length == 0) unitCode = "un";
            if (!unitByCode.TryGetValue(unitCode, out var unitId) && !newUnits.TryGetValue(unitCode, out unitId))
            {
                var uc = UnitOfMeasure.Create(tenant.CompanyId, unitCode, unitCode, "contagem", 1m);
                if (uc.IsFailure) { errors.Add($"Linha {line}: unidade '{unitCode}' inválida."); continue; }
                db.Units.Add(uc.Value);
                unitId = uc.Value.Id;
                newUnits[unitCode] = unitId;
            }

            var it = Item.Create(tenant.CompanyId, code, r.Name, unitId, r.Group, r.Ca);
            if (it.IsFailure) { errors.Add($"Linha {line}: {it.Error.Message}"); continue; }
            db.Items.Add(it.Value);
            db.StockBalances.Add(StockBalance.Create(tenant.CompanyId, it.Value.Id));
            imported++;
        }

        if (imported > 0)
        {
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                return new ItemImportResult(0, new[] { "Falha ao salvar a importação (possível código duplicado)." });
            }
            metrics.Record("materials.item.imported", tenant.CompanyId.Value.ToString());
        }
        return new ItemImportResult(imported, errors);
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
