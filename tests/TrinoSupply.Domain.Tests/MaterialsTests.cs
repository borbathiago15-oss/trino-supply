using TrinoSupply.BuildingBlocks;
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

public class ConsumptionTests
{
    private static readonly CompanyId Company = CompanyId.New();
    private static readonly CollaboratorId Colab = CollaboratorId.New();
    private static readonly (string, decimal)[] Lines = [("BOTA-42", 1m)];
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Result<Consumption> Make(string empresa = "EP1", string centro = "CC1", string motivo = "Nova contratação",
        (string, decimal)[]? lines = null) =>
        Consumption.Create(Company, empresa, centro, Colab, motivo, "almoxarife", lines ?? Lines, Now);

    [Fact]
    public void Baixa_valida_gera_linhas()
    {
        var c = Make().Value;
        Assert.Equal("EP1", c.CompanyCode);
        Assert.Equal("CC1", c.CostCenterCode);
        Assert.Equal("Nova contratação", c.Reason);
        Assert.Single(c.Lines);
        Assert.Equal("BOTA-42", c.Lines[0].ItemCode);
    }

    [Fact]
    public void Sem_empresa_falha()
    {
        var r = Make(empresa: "  ");
        Assert.True(r.IsFailure);
        Assert.Equal("materials.consumption.company_required", r.Error.Code);
    }

    [Fact]
    public void Sem_centro_falha()
    {
        var r = Make(centro: "  ");
        Assert.True(r.IsFailure);
        Assert.Equal("materials.consumption.cost_center_required", r.Error.Code);
    }

    [Fact]
    public void Sem_motivo_falha()
    {
        var r = Make(motivo: "  ");
        Assert.True(r.IsFailure);
        Assert.Equal("materials.consumption.reason_required", r.Error.Code);
    }

    [Fact]
    public void Sem_linhas_falha()
    {
        var r = Make(lines: []);
        Assert.True(r.IsFailure);
        Assert.Equal("materials.consumption.lines_required", r.Error.Code);
    }

    [Fact]
    public void Quantidade_nao_positiva_falha()
    {
        var r = Make(lines: [("BOTA-42", 0m)]);
        Assert.True(r.IsFailure);
        Assert.Equal("materials.consumption.qty_invalid", r.Error.Code);
    }
}

public class CollaboratorTests
{
    private static readonly CompanyId Company = CompanyId.New();

    [Fact]
    public void Cria_normaliza_codigos_opcionais()
    {
        var c = Collaborator.Create(Company, "  João Silva ", "m123", "cc1", "ep1").Value;
        Assert.Equal("João Silva", c.Name);
        Assert.Equal("m123", c.Registration);
        Assert.Equal("CC1", c.CostCenterCode);
        Assert.Equal("EP1", c.CompanyCode);
    }

    [Fact]
    public void Sem_nome_falha()
    {
        var r = Collaborator.Create(Company, "  ", null, null, null);
        Assert.True(r.IsFailure);
        Assert.Equal("materials.collaborator.name_required", r.Error.Code);
    }
}
