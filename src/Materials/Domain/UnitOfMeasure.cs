using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Materials.Domain;

/// <summary>
/// Unidade de medida (ADR-013). Cada unidade pertence a uma <see cref="Dimension"/> (ex.: "massa",
/// "contagem") e tem um fator para a unidade-base da dimensão. A conversão entre duas unidades da
/// MESMA dimensão é <c>qtd × fatorOrigem ÷ fatorDestino</c>. Converter entre dimensões é proibido.
/// </summary>
public sealed class UnitOfMeasure : AggregateRoot<UnitId>, IBelongsToTenant
{
    private UnitOfMeasure(UnitId id, CompanyId companyId, string code, string name, string dimension, decimal factorToBase)
        : base(id)
    {
        CompanyId = companyId;
        Code = code;
        Name = name;
        Dimension = dimension;
        FactorToBase = factorToBase;
    }

    // Exigido pelo EF Core.
    private UnitOfMeasure() : base(default!) { }

    public CompanyId CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Dimension { get; private set; } = string.Empty;

    /// <summary>Fator em relação à unidade-base da dimensão (ex.: g→1, kg→1000 se a base é g).</summary>
    public decimal FactorToBase { get; private set; }

    public static Result<UnitOfMeasure> Create(
        CompanyId companyId, string code, string name, string dimension, decimal factorToBase)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<UnitOfMeasure>(new Error("materials.unit.code_required", "Código da unidade é obrigatório."));
        if (string.IsNullOrWhiteSpace(dimension))
            return Result.Failure<UnitOfMeasure>(new Error("materials.unit.dimension_required", "Dimensão é obrigatória."));
        if (factorToBase <= 0)
            return Result.Failure<UnitOfMeasure>(new Error("materials.unit.factor_invalid", "Fator deve ser positivo."));

        return Result.Success(new UnitOfMeasure(
            UnitId.New(), companyId, code.Trim().ToLowerInvariant(), name.Trim(),
            dimension.Trim().ToLowerInvariant(), factorToBase));
    }

    /// <summary>Converte uma quantidade DESTA unidade para <paramref name="target"/> (mesma dimensão).</summary>
    public Result<decimal> ConvertTo(decimal quantity, UnitOfMeasure target)
    {
        if (!string.Equals(Dimension, target.Dimension, StringComparison.Ordinal))
            return Result.Failure<decimal>(new Error("materials.unit.dimension_mismatch",
                $"Não é possível converter de '{Dimension}' para '{target.Dimension}'."));

        return Result.Success(quantity * FactorToBase / target.FactorToBase);
    }
}
