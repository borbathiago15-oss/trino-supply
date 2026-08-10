using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Procurement.Domain;

public readonly record struct SupplierId(Guid Value)
{
    public static SupplierId New() => new(Guid.NewGuid());
    public static SupplierId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("SupplierId não pode ser vazio.", nameof(value))
        : new SupplierId(value);
    public override string ToString() => Value.ToString();
}

public enum SupplierStatus
{
    Active = 1,
    Inactive = 2
}

/// <summary>
/// Fornecedor (PR-001 / PRC-005, base cadastral). Código único no tenant. Guarda os dados fiscais
/// usados na emissão da OC (endereço, condição/forma de pagamento) — na concorrência/BID, o vencedor
/// é o fornecedor selecionado na emissão do pedido.
/// </summary>
public sealed class Supplier : AggregateRoot<SupplierId>, IBelongsToTenant
{
    private Supplier(
        SupplierId id, CompanyId companyId, string code, string name, string taxId,
        string stateRegistration, string address, string district, string city, string state,
        string zipCode, string phone, string email, string paymentTerms, string paymentMethod) : base(id)
    {
        CompanyId = companyId;
        Code = code;
        Name = name;
        TaxId = taxId;
        StateRegistration = stateRegistration;
        Address = address;
        District = district;
        City = city;
        State = state;
        ZipCode = zipCode;
        Phone = phone;
        Email = email;
        PaymentTerms = paymentTerms;
        PaymentMethod = paymentMethod;
        Status = SupplierStatus.Active;
    }

    private Supplier() : base(default!) { } // EF

    public CompanyId CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string TaxId { get; private set; } = string.Empty;          // CNPJ
    public string StateRegistration { get; private set; } = string.Empty; // Inscrição Estadual
    public string Address { get; private set; } = string.Empty;         // Logradouro
    public string District { get; private set; } = string.Empty;        // Bairro
    public string City { get; private set; } = string.Empty;            // Cidade
    public string State { get; private set; } = string.Empty;           // UF
    public string ZipCode { get; private set; } = string.Empty;         // CEP
    public string Phone { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PaymentTerms { get; private set; } = string.Empty;    // Cond. Pgto (ex.: "A Vista")
    public string PaymentMethod { get; private set; } = string.Empty;   // Forma Pgto (ex.: "Depósito Bancário")
    public SupplierStatus Status { get; private set; }

    public static Result<Supplier> Create(
        CompanyId companyId, string code, string name, string taxId,
        string? stateRegistration = null, string? address = null, string? district = null,
        string? city = null, string? state = null, string? zipCode = null, string? phone = null,
        string? email = null, string? paymentTerms = null, string? paymentMethod = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<Supplier>(new Error("purchases.supplier.code_required", "Código do fornecedor é obrigatório."));
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Supplier>(new Error("purchases.supplier.name_required", "Nome do fornecedor é obrigatório."));

        return Result.Success(new Supplier(
            SupplierId.New(), companyId, code.Trim().ToUpperInvariant(), name.Trim(), (taxId ?? string.Empty).Trim(),
            (stateRegistration ?? string.Empty).Trim(), (address ?? string.Empty).Trim(),
            (district ?? string.Empty).Trim(), (city ?? string.Empty).Trim(),
            (state ?? string.Empty).Trim().ToUpperInvariant(), (zipCode ?? string.Empty).Trim(),
            (phone ?? string.Empty).Trim(), (email ?? string.Empty).Trim(),
            (paymentTerms ?? string.Empty).Trim(), (paymentMethod ?? string.Empty).Trim()));
    }

    public Result Update(
        string name, string taxId, string? stateRegistration, string? address, string? district,
        string? city, string? state, string? zipCode, string? phone, string? email,
        string? paymentTerms, string? paymentMethod)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(new Error("purchases.supplier.name_required", "Nome do fornecedor é obrigatório."));

        Name = name.Trim();
        TaxId = (taxId ?? string.Empty).Trim();
        StateRegistration = (stateRegistration ?? string.Empty).Trim();
        Address = (address ?? string.Empty).Trim();
        District = (district ?? string.Empty).Trim();
        City = (city ?? string.Empty).Trim();
        State = (state ?? string.Empty).Trim().ToUpperInvariant();
        ZipCode = (zipCode ?? string.Empty).Trim();
        Phone = (phone ?? string.Empty).Trim();
        Email = (email ?? string.Empty).Trim();
        PaymentTerms = (paymentTerms ?? string.Empty).Trim();
        PaymentMethod = (paymentMethod ?? string.Empty).Trim();
        Version++;
        return Result.Success();
    }
}
