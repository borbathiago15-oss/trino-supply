using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Materials.Domain;

/// <summary>
/// Política de reposição de um item (ADR-014). Identidade = o próprio item. Quando o saldo cai a
/// (ou abaixo de) <see cref="MinLevel"/> (ponto de reposição), sugere-se repor até <see cref="MaxLevel"/>
/// (nível-alvo). É a base do ciclo "estoque baixo → sugestão de compra" (ponte para Procurement).
/// </summary>
public sealed class ReplenishmentPolicy : AggregateRoot<ItemId>, IBelongsToTenant
{
    private ReplenishmentPolicy(ItemId itemId, CompanyId companyId, decimal minLevel, decimal maxLevel) : base(itemId)
    {
        CompanyId = companyId;
        MinLevel = minLevel;
        MaxLevel = maxLevel;
        Active = true;
    }

    // Exigido pelo EF Core.
    private ReplenishmentPolicy() : base(default!) { }

    public CompanyId CompanyId { get; private set; }

    /// <summary>Ponto de reposição: repor quando o saldo ≤ este nível.</summary>
    public decimal MinLevel { get; private set; }

    /// <summary>Nível-alvo: repor até esta quantidade.</summary>
    public decimal MaxLevel { get; private set; }
    public bool Active { get; private set; }

    public static Result<ReplenishmentPolicy> Define(CompanyId companyId, ItemId itemId, decimal minLevel, decimal maxLevel)
    {
        var validation = Validate(minLevel, maxLevel);
        if (validation.IsFailure) return Result.Failure<ReplenishmentPolicy>(validation.Error);
        return Result.Success(new ReplenishmentPolicy(itemId, companyId, minLevel, maxLevel));
    }

    public Result Update(decimal minLevel, decimal maxLevel)
    {
        var validation = Validate(minLevel, maxLevel);
        if (validation.IsFailure) return validation;

        MinLevel = minLevel;
        MaxLevel = maxLevel;
        Active = true;
        Version++;
        return Result.Success();
    }

    /// <summary>Necessidade de reposição dado o saldo atual: <c>máx − saldo</c> quando saldo ≤ mín; senão 0.</summary>
    public decimal NeedFor(decimal balance) => Active && balance <= MinLevel ? MaxLevel - balance : 0m;

    private static Result Validate(decimal minLevel, decimal maxLevel)
    {
        if (minLevel < 0)
            return Result.Failure(new Error("materials.replenishment.min_invalid", "Nível mínimo não pode ser negativo."));
        if (maxLevel <= minLevel)
            return Result.Failure(new Error("materials.replenishment.max_invalid", "Nível máximo deve ser maior que o mínimo."));
        return Result.Success();
    }
}
