using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.Materials.Domain;
using Xunit;

namespace TrinoSupply.Domain.Tests;

public class UnitOfMeasureTests
{
    private static readonly CompanyId Company = CompanyId.New();

    [Fact]
    public void Converte_dentro_da_mesma_dimensao()
    {
        var g = UnitOfMeasure.Create(Company, "g", "grama", "massa", 1m).Value;
        var kg = UnitOfMeasure.Create(Company, "kg", "quilo", "massa", 1000m).Value;

        Assert.Equal(2000m, kg.ConvertTo(2m, g).Value);   // 2 kg -> 2000 g
        Assert.Equal(1.5m, g.ConvertTo(1500m, kg).Value);  // 1500 g -> 1.5 kg
    }

    [Fact]
    public void Converter_entre_dimensoes_diferentes_falha()
    {
        var kg = UnitOfMeasure.Create(Company, "kg", "quilo", "massa", 1000m).Value;
        var un = UnitOfMeasure.Create(Company, "un", "unidade", "contagem", 1m).Value;

        var result = kg.ConvertTo(1m, un);
        Assert.True(result.IsFailure);
        Assert.Equal("materials.unit.dimension_mismatch", result.Error.Code);
    }

    [Fact]
    public void Fator_nao_positivo_falha()
    {
        Assert.True(UnitOfMeasure.Create(Company, "x", "x", "massa", 0m).IsFailure);
        Assert.True(UnitOfMeasure.Create(Company, "x", "x", "massa", -1m).IsFailure);
    }
}

public class StockBalanceTests
{
    private static readonly CompanyId Company = CompanyId.New();

    [Fact]
    public void Apply_entrada_e_saida_ajusta_saldo()
    {
        var balance = StockBalance.Create(Company, ItemId.New());
        balance.Apply(100m);
        balance.Apply(-30m);

        Assert.Equal(70m, balance.Quantity);
    }

    [Fact]
    public void Apply_que_deixaria_negativo_falha_e_nao_altera()
    {
        var balance = StockBalance.Create(Company, ItemId.New());
        balance.Apply(10m);

        var result = balance.Apply(-11m);
        Assert.True(result.IsFailure);
        Assert.Equal("materials.stock.insufficient", result.Error.Code);
        Assert.Equal(10m, balance.Quantity);
    }
}

public class ReplenishmentPolicyTests
{
    private static readonly CompanyId Company = CompanyId.New();

    [Fact]
    public void Max_menor_ou_igual_min_falha()
    {
        Assert.True(ReplenishmentPolicy.Define(Company, ItemId.New(), 50m, 30m).IsFailure);
        Assert.True(ReplenishmentPolicy.Define(Company, ItemId.New(), 10m, 10m).IsFailure);
    }

    [Theory]
    [InlineData(5, 10, 100, 95)]   // saldo <= min -> repor até max
    [InlineData(0, 15, 60, 60)]
    [InlineData(50, 20, 80, 0)]    // saldo > min -> sem necessidade
    public void NeedFor_calcula_necessidade(decimal balance, decimal min, decimal max, decimal expected)
    {
        var policy = ReplenishmentPolicy.Define(Company, ItemId.New(), min, max).Value;
        Assert.Equal(expected, policy.NeedFor(balance));
    }
}
