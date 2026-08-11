using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Procurement.Domain;

public readonly record struct CostCenterId(Guid Value)
{
    public static CostCenterId New() => new(Guid.NewGuid());
    public static CostCenterId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("CostCenterId não pode ser vazio.", nameof(value))
        : new CostCenterId(value);
    public override string ToString() => Value.ToString();
}

public enum CostCenterStatus { Active = 1, Inactive = 2 }

/// <summary>
/// Centro de custo (spec Sistema de Compras). Cadastro por tenant (RLS): código único + nome. Usado no
/// cabeçalho da solicitação de compra para recortar gastos por centro nos dashboards.
/// </summary>
public sealed class CostCenter : AggregateRoot<CostCenterId>, IBelongsToTenant
{
    private CostCenter(CostCenterId id, CompanyId companyId, string code, string name, PayingCompanyId? payingCompanyId) : base(id)
    {
        CompanyId = companyId;
        Code = code;
        Name = name;
        PayingCompanyId = payingCompanyId;
        Status = CostCenterStatus.Active;
    }

    private CostCenter() : base(default!) { } // EF

    public CompanyId CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    /// <summary>CNPJ interno do grupo vinculado a este centro (v2) — quem paga os custos do centro.</summary>
    public PayingCompanyId? PayingCompanyId { get; private set; }
    public CostCenterStatus Status { get; private set; }

    public static Result<CostCenter> Create(CompanyId companyId, string code, string name, PayingCompanyId? payingCompanyId = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<CostCenter>(new Error("purchases.cost_center.code_required", "Código do centro de custo é obrigatório."));
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<CostCenter>(new Error("purchases.cost_center.name_required", "Nome do centro de custo é obrigatório."));

        return Result.Success(new CostCenter(CostCenterId.New(), companyId, code.Trim().ToUpperInvariant(), name.Trim(), payingCompanyId));
    }

    public void LinkPayingCompany(PayingCompanyId? payingCompanyId)
    {
        PayingCompanyId = payingCompanyId;
        Version++;
    }

    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(new Error("purchases.cost_center.name_required", "Nome do centro de custo é obrigatório."));
        Name = name.Trim();
        Version++;
        return Result.Success();
    }
}
