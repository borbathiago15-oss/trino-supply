using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Catalog;

/// <summary>
/// Serviço do catálogo (MMS-002 MVP). Manutenção restrita ao mantenedor
/// (SupplyManager/SystemAdministrator); consulta aberta aos papéis do módulo de requisições.
/// </summary>
/// <summary>Fornecedor informado no cadastro do produto (não precisa estar cadastrado na plataforma).</summary>
public record ItemSupplierInput(string SupplierName, string? TaxId, string? Contact,
    string? SupplierItemCode, decimal? LastPrice, string? Notes);

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

    public async Task<List<CatalogItem>> ListAsync(string? family, string? q, bool includeInactive,
        bool stockOnly = false, CancellationToken ct = default)
    {
        var query = db.CatalogItems.AsQueryable();
        if (!includeInactive) query = query.Where(i => i.Active);
        if (stockOnly) query = query.Where(i => i.StockControlled);   // almoxarifado
        if (!string.IsNullOrWhiteSpace(family)) query = query.Where(i => i.Family == family.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            query = query.Where(i => i.Description.ToLower().Contains(term) || i.Code.ToLower().Contains(term));
        }
        return await query.Include(i => i.Suppliers)
            .OrderBy(i => i.Family).ThenBy(i => i.Description).Take(500).ToListAsync(ct);
    }

    public async Task<(CatalogItem? item, UserError? error)> CreateAsync(
        Guid actorId, string? code, string description, string family, string? unit, decimal? referencePrice,
        bool stockControlled = false, decimal? minimumQty = null,
        IReadOnlyList<ItemSupplierInput>? suppliers = null, CancellationToken ct = default)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        family = family.Trim().ToUpperInvariant();
        if (description.Trim().Length < 3) return (null, new("IC-ERR-013", "Descreva o item (mín. 3 caracteres)."));
        if (family.Length < 3) return (null, new("IC-ERR-014", "Informe a família do item (ex.: MATERIAL DE LIMPEZA)."));
        if (referencePrice is < 0) return (null, new("IC-ERR-015", "O preço de referência não pode ser negativo."));
        if (minimumQty is < 0) return (null, new("IC-ERR-016", "O estoque mínimo não pode ser negativo."));

        if (code.Length == 0) code = await GenerateCodeAsync(family, ct);   // regra automática pela família
        else if (code.Length < 2) return (null, new("IC-ERR-012", "O código do item precisa ter ao menos 2 caracteres."));
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
            StockControlled = stockControlled,
            MinimumQty = minimumQty,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actorId,
        };
        db.CatalogItems.Add(item);
        ReplaceSuppliers(item, suppliers, now);
        await db.SaveChangesAsync(ct);
        return (item, null);
    }

    /// <summary>Código automático: 3 letras da família (sem acento) + sequência — ex.: MAT-001.</summary>
    private async Task<string> GenerateCodeAsync(string family, CancellationToken ct)
    {
        var letters = new string(family.Normalize(System.Text.NormalizationForm.FormD)
            .Where(char.IsLetter).ToArray()).ToUpperInvariant();
        var prefix = letters.Length >= 2 ? letters[..Math.Min(3, letters.Length)] : "ITM";
        var seq = await db.CatalogItems.CountAsync(i => i.Code.StartsWith(prefix + "-"), ct) + 1;
        var code = $"{prefix}-{seq:000}";
        while (await db.CatalogItems.AnyAsync(i => i.Code == code, ct)) code = $"{prefix}-{++seq:000}";
        return code;
    }

    /// <summary>Substitui a lista de fornecedores do produto (texto livre; vazio remove todos).</summary>
    private void ReplaceSuppliers(CatalogItem item, IReadOnlyList<ItemSupplierInput>? suppliers, DateTimeOffset now)
    {
        if (suppliers is null) return;
        if (item.Suppliers.Count > 0) db.CatalogItemSuppliers.RemoveRange(item.Suppliers);
        item.Suppliers.Clear();
        foreach (var s in suppliers.Where(s => !string.IsNullOrWhiteSpace(s.SupplierName)))
        {
            var link = new CatalogItemSupplier
            {
                CatalogItemId = item.Id,
                SupplierName = s.SupplierName.Trim(),
                TaxId = Digits(s.TaxId),
                Contact = Clean(s.Contact),
                SupplierItemCode = Clean(s.SupplierItemCode),
                LastPrice = s.LastPrice is < 0 ? null : s.LastPrice,
                Notes = Clean(s.Notes),
                CreatedAt = now,
            };
            // só o Add explícito: o fix-up do EF liga o vínculo à navegação pela FK
            db.CatalogItemSuppliers.Add(link);
        }
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string? Digits(string? s)
    {
        var d = new string((s ?? "").Where(char.IsDigit).ToArray());
        return d.Length == 0 ? null : d;
    }

    public async Task<(CatalogItem? item, UserError? error)> UpdateAsync(
        Guid id, string? description, string? family, string? unit, decimal? referencePrice, bool? active,
        bool? stockControlled = null, decimal? minimumQty = null, bool clearMinimum = false,
        IReadOnlyList<ItemSupplierInput>? suppliers = null, CancellationToken ct = default)
    {
        var item = await db.CatalogItems.Include(i => i.Suppliers).SingleOrDefaultAsync(i => i.Id == id, ct);
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
        if (stockControlled is not null) item.StockControlled = stockControlled.Value;
        if (clearMinimum) item.MinimumQty = null;
        else if (minimumQty is not null)
        {
            if (minimumQty < 0) return (null, new("IC-ERR-016", "O estoque mínimo não pode ser negativo."));
            item.MinimumQty = minimumQty;
        }
        if (active is not null) item.Active = active.Value;
        var now = clock.GetUtcNow();
        ReplaceSuppliers(item, suppliers, now);
        item.UpdatedAt = now;
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
