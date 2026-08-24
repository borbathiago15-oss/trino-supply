using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Users;

/// <summary>Cadastro dos CNPJs do grupo (empresas). Cada CC pode apontar para um CNPJ.</summary>
public class CompanyService(AppDbContext db, TimeProvider clock)
{
    public static bool CanMaintain(string role) =>
        role is Roles.SupplyManager or Roles.SystemAdministrator;

    public Task<List<Company>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        var q = db.Companies.AsQueryable();
        if (!includeInactive) q = q.Where(c => c.Active);
        return q.OrderBy(c => c.LegalName).Take(200).ToListAsync(ct);
    }

    public async Task<(Company? company, UserError? error)> CreateAsync(
        string legalName, string taxId, string? stateRegistration, string address, string? district,
        string city, string state, string zip, string? phone, string? email, CancellationToken ct = default)
    {
        if (legalName.Trim().Length < 3)
            return (null, new("EMP-ERR-010", "Informe a razão social da empresa."));
        var digits = OnlyDigits(taxId);
        if (digits.Length != 14)
            return (null, new("EMP-ERR-011", "CNPJ inválido: informe os 14 dígitos."));
        if (await db.Companies.AnyAsync(c => c.TaxId == digits, ct))
            return (null, new("EMP-ERR-012", "Já existe uma empresa cadastrada com este CNPJ."));
        if (address.Trim().Length < 3 || city.Trim().Length < 2 || state.Trim().Length != 2)
            return (null, new("EMP-ERR-013", "Informe endereço, cidade e UF (2 letras) da empresa."));

        var now = clock.GetUtcNow();
        var company = new Company
        {
            LegalName = legalName.Trim(),
            TaxId = digits,
            StateRegistration = Clean(stateRegistration),
            Address = address.Trim(),
            District = Clean(district),
            City = city.Trim(),
            State = state.Trim().ToUpperInvariant(),
            Zip = zip.Trim(),
            Phone = Clean(phone),
            Email = Clean(email),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync(ct);
        return (company, null);
    }

    public async Task<(Company? company, UserError? error)> UpdateAsync(
        Guid id, string? legalName, string? stateRegistration, string? address, string? district,
        string? city, string? state, string? zip, string? phone, string? email, bool? active,
        CancellationToken ct = default)
    {
        var company = await db.Companies.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (company is null) return (null, new("EMP-ERR-404", "Empresa não encontrada."));
        if (legalName is not null && legalName.Trim().Length >= 3) company.LegalName = legalName.Trim();
        if (stateRegistration is not null) company.StateRegistration = Clean(stateRegistration);
        if (address is not null && address.Trim().Length >= 3) company.Address = address.Trim();
        if (district is not null) company.District = Clean(district);
        if (city is not null && city.Trim().Length >= 2) company.City = city.Trim();
        if (state is not null && state.Trim().Length == 2) company.State = state.Trim().ToUpperInvariant();
        if (zip is not null) company.Zip = zip.Trim();
        if (phone is not null) company.Phone = Clean(phone);
        if (email is not null) company.Email = Clean(email);
        if (active is not null) company.Active = active.Value;
        company.UpdatedAt = clock.GetUtcNow();
        company.Version += 1;
        await db.SaveChangesAsync(ct);
        return (company, null);
    }

    private static string OnlyDigits(string s) => new(s.Where(char.IsDigit).ToArray());
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
