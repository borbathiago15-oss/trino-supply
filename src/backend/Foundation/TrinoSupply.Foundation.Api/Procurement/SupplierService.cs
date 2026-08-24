using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>Serviço de fornecedores (SUP-001 MVP).</summary>
public class SupplierService(AppDbContext db, TimeProvider clock)
{
    public static bool CanMaintain(string role) =>
        role is Roles.PurchasingOfficer or Roles.SupplyManager or Roles.SystemAdministrator;

    public static bool CanView(string role) => CanMaintain(role) || role == Roles.Auditor;

    public Task<List<Supplier>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        var q = db.Suppliers.AsQueryable();
        if (!includeInactive) q = q.Where(s => s.Active);
        return q.OrderBy(s => s.LegalName).Take(500).ToListAsync(ct);
    }

    public async Task<(Supplier? supplier, UserError? error)> CreateAsync(
        Guid actorId, string legalName, string? tradeName, string taxId, string? email, string? phone,
        CancellationToken ct = default)
    {
        legalName = legalName.Trim();
        if (legalName.Length < 3) return (null, new("SUP-ERR-012", "Informe a razão social (mín. 3 caracteres)."));
        var digits = new string((taxId ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length is not (11 or 14))
            return (null, new("SUP-ERR-011", "CPF/CNPJ inválido: informe 11 ou 14 dígitos."));
        if (await db.Suppliers.AnyAsync(s => s.TaxId == digits, ct))
            return (null, new("SUP-ERR-010", "Já existe um fornecedor com este CPF/CNPJ."));

        var now = clock.GetUtcNow();
        var supplier = new Supplier
        {
            LegalName = legalName,
            TradeName = string.IsNullOrWhiteSpace(tradeName) ? null : tradeName.Trim(),
            TaxId = digits,
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = actorId,
        };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(ct);
        return (supplier, null);
    }

    public async Task<(Supplier? supplier, UserError? error)> UpdateAsync(
        Guid id, string? tradeName, string? email, string? phone, bool? active, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return (null, new("SUP-ERR-404", "Fornecedor não encontrado."));

        if (tradeName is not null) supplier.TradeName = string.IsNullOrWhiteSpace(tradeName) ? null : tradeName.Trim();
        if (email is not null) supplier.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (phone is not null) supplier.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (active is not null) supplier.Active = active.Value;
        supplier.UpdatedAt = clock.GetUtcNow();
        supplier.Version += 1;
        await db.SaveChangesAsync(ct);
        return (supplier, null);
    }
}
