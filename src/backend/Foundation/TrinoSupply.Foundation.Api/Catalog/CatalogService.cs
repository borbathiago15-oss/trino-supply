using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Catalog;

/// <summary>
/// Serviço do catálogo (MMS-002 MVP). Manutenção restrita ao mantenedor
/// (SupplyManager/SystemAdministrator); consulta aberta aos papéis do módulo de requisições.
/// </summary>
public class CatalogService(AppDbContext db, TimeProvider clock)
{
    public static bool CanMaintain(string role) =>
        role is Roles.SupplyManager or Roles.SystemAdministrator;

    public async Task<List<string>> FamiliesAsync(bool onlyActive, CancellationToken ct = default)
    {
        var query = db.CatalogItems.AsQueryable();
        if (onlyActive) query = query.Where(i => i.Active);
        return await query.Select(i => i.Family).Distinct().OrderBy(f => f).ToListAsync(ct);
    }

    public async Task<List<CatalogItem>> ListAsync(string? family, string? q, bool includeInactive, CancellationToken ct = default)
    {
        var query = db.CatalogItems.AsQueryable();
        if (!includeInactive) query = query.Where(i => i.Active);
        if (!string.IsNullOrWhiteSpace(family)) query = query.Where(i => i.Family == family.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            query = query.Where(i => i.Description.ToLower().Contains(term) || i.Code.ToLower().Contains(term));
        }
        return await query.OrderBy(i => i.Family).ThenBy(i => i.Description).Take(500).ToListAsync(ct);
    }

    public async Task<(CatalogItem? item, UserError? error)> CreateAsync(
        Guid actorId, string code, string description, string family, string? unit, decimal? referencePrice,
        CancellationToken ct = default)
    {
        code = code.Trim().ToUpperInvariant();
        family = family.Trim().ToUpperInvariant();
        if (code.Length < 2) return (null, new("IC-ERR-012", "Informe o código do item (mín. 2 caracteres)."));
        if (description.Trim().Length < 3) return (null, new("IC-ERR-013", "Descreva o item (mín. 3 caracteres)."));
        if (family.Length < 3) return (null, new("IC-ERR-014", "Informe a família do item (ex.: MATERIAL DE LIMPEZA)."));
        if (referencePrice is < 0) return (null, new("IC-ERR-015", "O preço de referência não pode ser negativo."));
        if (await db.CatalogItems.AnyAsync(i => i.Code == code, ct))
            return (null, new("IC-ERR-010", "Já existe um item com este código."));

        var now = clock.GetUtcNow();
        var item = new CatalogItem
        {
            Code = code,
            Description = description.Trim(),
            Family = family,
            UnitOfMeasure = string.IsNullOrWhiteSpace(unit) ? "UN" : unit.Trim().ToUpperInvariant(),
            ReferencePrice = referencePrice,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actorId,
        };
        db.CatalogItems.Add(item);
        await db.SaveChangesAsync(ct);
        return (item, null);
    }

    public async Task<(CatalogItem? item, UserError? error)> UpdateAsync(
        Guid id, string? description, string? family, string? unit, decimal? referencePrice, bool? active,
        CancellationToken ct = default)
    {
        var item = await db.CatalogItems.SingleOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return (null, new("IC-ERR-404", "Item não encontrado."));
        if (description is not null)
        {
            if (description.Trim().Length < 3) return (null, new("IC-ERR-013", "Descreva o item (mín. 3 caracteres)."));
            item.Description = description.Trim();
        }
        if (family is not null)
        {
            var f = family.Trim().ToUpperInvariant();
            if (f.Length < 3) return (null, new("IC-ERR-014", "Família inválida."));
            item.Family = f;
        }
        if (unit is not null && !string.IsNullOrWhiteSpace(unit)) item.UnitOfMeasure = unit.Trim().ToUpperInvariant();
        if (referencePrice is not null)
        {
            if (referencePrice < 0) return (null, new("IC-ERR-015", "O preço de referência não pode ser negativo."));
            item.ReferencePrice = referencePrice;
        }
        if (active is not null) item.Active = active.Value;
        item.UpdatedAt = clock.GetUtcNow();
        item.Version += 1;
        await db.SaveChangesAsync(ct);
        return (item, null);
    }

    /// <summary>Resolve itens do catálogo para uma requisição: só Ativos (IC-BR-001), com snapshot.</summary>
    public async Task<(Dictionary<Guid, CatalogItem>? items, UserError? error)> ResolveForRequisitionAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return (new Dictionary<Guid, CatalogItem>(), null);
        var found = await db.CatalogItems.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        foreach (var id in ids)
        {
            if (!found.TryGetValue(id, out var item))
                return (null, new("PR-ERR-022", "Item do catálogo inexistente."));
            if (!item.Active)
                return (null, new("PR-ERR-022", $"O item '{item.Description}' está inativo no catálogo e não pode ser solicitado."));
        }
        return (found, null);
    }
}
