using TrinoSupply.Foundation.Api.Analytics;

namespace TrinoSupply.Foundation.Tests;

public class BuyerScoreTests
{
    [Fact]
    public async Task Mais_rapido_com_melhor_OTIF_e_saving_pontua_100()
    {
        await Task.CompletedTask;
        var rows = BuyerScore.Compute(
        [
            new("Carla", OtifPercent: 100, AvgDaysToPo: 2, SavingTotal: 100, PoValue: 1000),
            new("Pedro", OtifPercent: 50, AvgDaysToPo: 8, SavingTotal: 0, PoValue: 1000),
        ]);

        var carla = rows.Single(r => r.Label == "Carla");
        var pedro = rows.Single(r => r.Label == "Pedro");
        Assert.Equal(100, carla.Score);                     // melhor em tudo
        Assert.Equal(100, carla.SpeedPct);
        Assert.Equal(100, carla.SavingPct);
        Assert.True(pedro.Score < carla.Score);
        Assert.Equal(Math.Round(3.0 / 9 * 100, 1), pedro.SpeedPct);   // (2+1)/(8+1)
        Assert.Equal(0, pedro.SavingPct);
    }

    [Fact]
    public void Componente_sem_dado_sai_da_conta()
    {
        var rows = BuyerScore.Compute(
        [
            new("Sem OC", OtifPercent: null, AvgDaysToPo: null, SavingTotal: 0, PoValue: 0),
            new("Com tudo", OtifPercent: 80, AvgDaysToPo: 3, SavingTotal: 50, PoValue: 500),
        ]);

        Assert.Null(rows.Single(r => r.Label == "Sem OC").Score);    // nada medido
        Assert.NotNull(rows.Single(r => r.Label == "Com tudo").Score);
    }

    [Fact]
    public void Sem_saving_no_periodo_o_componente_nao_penaliza_ninguem()
    {
        var rows = BuyerScore.Compute(
        [
            new("A", OtifPercent: 100, AvgDaysToPo: 1, SavingTotal: 0, PoValue: 1000),
            new("B", OtifPercent: 100, AvgDaysToPo: 1, SavingTotal: 0, PoValue: 2000),
        ]);
        Assert.All(rows, r => Assert.Equal(100, r.Score));   // saving sai da conta para todos
        Assert.All(rows, r => Assert.Null(r.SavingPct));
    }
}
