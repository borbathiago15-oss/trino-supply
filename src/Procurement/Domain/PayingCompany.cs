using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Procurement.Domain;

public readonly record struct PayingCompanyId(Guid Value)
{
    public static PayingCompanyId New() => new(Guid.NewGuid());
    public static PayingCompanyId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("PayingCompanyId não pode ser vazio.", nameof(value))
        : new PayingCompanyId(value);
    public override string ToString() => Value.ToString();
}

public enum PayingCompanyStatus
{
    Active = 1,
    Inactive = 2
}

/// <summary>
/// Empresa pagadora (registro de CNPJs do grupo). Na emissão da OC o usuário escolhe qual
/// empresa/CNPJ é a responsável pelo pagamento — vira o cabeçalho comprador do documento.
/// É um cadastro <b>por tenant</b> (RLS), independente da <c>Company</c> do Foundation: um mesmo
/// tenant pode ter vários CNPJs pagadores. Código único no tenant.
/// </summary>
public sealed class PayingCompany : AggregateRoot<PayingCompanyId>, IBelongsToTenant
{
    private PayingCompany(
        PayingCompanyId id, CompanyId companyId, string code, string legalName, string taxId,
        string stateRegistration, string address, string district, string city, string state,
        string zipCode, string phone, string email) : base(id)
    {
        CompanyId = companyId;
        Code = code;
        LegalName = legalName;
        TaxId = taxId;
        StateRegistration = stateRegistration;
        Address = address;
        District = district;
        City = city;
        State = state;
        ZipCode = zipCode;
        Phone = phone;
        Email = email;
        Status = PayingCompanyStatus.Active;
    }

    private PayingCompany() : base(default!) { } // EF

    public CompanyId CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string LegalName { get; private set; } = string.Empty;
    public string TaxId { get; private set; } = string.Empty;          // CNPJ
    public string StateRegistration { get; private set; } = string.Empty; // Inscrição Estadual
    public string Address { get; private set; } = string.Empty;         // Logradouro
    public string District { get; private set; } = string.Empty;        // Bairro
    public string City { get; private set; } = string.Empty;            // Cidade
    public string State { get; private set; } = string.Empty;           // UF
    public string ZipCode { get; private set; } = string.Empty;         // CEP
    public string Phone { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public PayingCompanyStatus Status { get; private set; }

    public static Result<PayingCompany> Create(
        CompanyId companyId, string code, string legalName, string taxId, string? stateRegistration,
        string? address, string? district, string? city, string? state, string? zipCode,
        string? phone, string? email)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<PayingCompany>(new Error("purchases.paying_company.code_required", "Código da empresa pagadora é obrigatório."));
        if (string.IsNullOrWhiteSpace(legalName))
            return Result.Failure<PayingCompany>(new Error("purchases.paying_company.name_required", "Razão social é obrigatória."));
        if (string.IsNullOrWhiteSpace(taxId))
            return Result.Failure<PayingCompany>(new Error("purchases.paying_company.taxid_required", "CNPJ é obrigatório."));

        return Result.Success(new PayingCompany(
            PayingCompanyId.New(), companyId, code.Trim().ToUpperInvariant(), legalName.Trim(), taxId.Trim(),
            (stateRegistration ?? string.Empty).Trim(), (address ?? string.Empty).Trim(),
            (district ?? string.Empty).Trim(), (city ?? string.Empty).Trim(),
            (state ?? string.Empty).Trim().ToUpperInvariant(), (zipCode ?? string.Empty).Trim(),
            (phone ?? string.Empty).Trim(), (email ?? string.Empty).Trim()));
    }

    public Result Update(
        string legalName, string taxId, string? stateRegistration, string? address, string? district,
        string? city, string? state, string? zipCode, string? phone, string? email)
    {
        if (string.IsNullOrWhiteSpace(legalName))
            return Result.Failure(new Error("purchases.paying_company.name_required", "Razão social é obrigatória."));
        if (string.IsNullOrWhiteSpace(taxId))
            return Result.Failure(new Error("purchases.paying_company.taxid_required", "CNPJ é obrigatório."));

        LegalName = legalName.Trim();
        TaxId = taxId.Trim();
        StateRegistration = (stateRegistration ?? string.Empty).Trim();
        Address = (address ?? string.Empty).Trim();
        District = (district ?? string.Empty).Trim();
        City = (city ?? string.Empty).Trim();
        State = (state ?? string.Empty).Trim().ToUpperInvariant();
        ZipCode = (zipCode ?? string.Empty).Trim();
        Phone = (phone ?? string.Empty).Trim();
        Email = (email ?? string.Empty).Trim();
        Version++;
        return Result.Success();
    }

    public void Deactivate() { Status = PayingCompanyStatus.Inactive; Version++; }
    public void Activate() { Status = PayingCompanyStatus.Active; Version++; }
}
