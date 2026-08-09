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

/// <summary>Fornecedor (PR-001 / PRC-005, base cadastral). Código único no tenant.</summary>
public sealed class Supplier : AggregateRoot<SupplierId>, IBelongsToTenant
{
    private Supplier(SupplierId id, CompanyId companyId, string code, string name, string taxId) : base(id)
    {
        CompanyId = companyId;
        Code = code;
        Name = name;
        TaxId = taxId;
        Status = SupplierStatus.Active;
    }

    private Supplier() : base(default!) { } // EF

    public CompanyId CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string TaxId { get; private set; } = string.Empty;
    public SupplierStatus Status { get; private set; }

    public static Result<Supplier> Create(CompanyId companyId, string code, string name, string taxId)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<Supplier>(new Error("purchases.supplier.code_required", "Código do fornecedor é obrigatório."));
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Supplier>(new Error("purchases.supplier.name_required", "Nome do fornecedor é obrigatório."));

        return Result.Success(new Supplier(
            SupplierId.New(), companyId, code.Trim().ToUpperInvariant(), name.Trim(), (taxId ?? string.Empty).Trim()));
    }
}
