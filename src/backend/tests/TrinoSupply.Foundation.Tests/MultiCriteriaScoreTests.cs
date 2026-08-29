using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

public class MultiCriteriaScoreTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    [Fact]
    public void Melhor_preco_e_melhor_prazo_pontuam_100_e_o_score_ordena_a_disputa()
    {
        var rows = MultiCriteriaScore.Compute(
        [
            new(A, "Alfa", 1000m, 10, 30, 90, 10),
            new(B, "Beta", 1200m, 20, 45, 60, 40),
        ]);

        Assert.Equal(2, rows.Count);
        var alfa = rows.Single(r => r.SupplierId == A);
        var beta = rows.Single(r => r.SupplierId == B);
        Assert.Equal(100, alfa.PricePct);           // menor preço
        Assert.Equal(100, alfa.DeliveryPct);        // menor prazo de entrega
        Assert.Equal(100, beta.PaymentPct);         // maior prazo de pagamento
        Assert.Equal(Math.Round(1000.0 / 1200 * 100, 1), beta.PricePct);
        Assert.True(alfa.Score > beta.Score);
        Assert.Equal(A, rows[0].SupplierId);        // ordenado do melhor para o pior
    }

    [Fact]
    public void Componente_sem_dado_sai_da_conta_com_pesos_renormalizados()
    {
        var rows = MultiCriteriaScore.Compute(
        [
            new(A, "Alfa", 1000m, null, null, null, null),   // só preço
            new(B, "Beta", 2000m, null, null, null, null),
        ]);

        Assert.Equal(100, rows.Single(r => r.SupplierId == A).Score);   // preço vale 100% do peso
        Assert.Equal(50, rows.Single(r => r.SupplierId == B).Score);
        Assert.Null(rows[0].OtifPct);
        Assert.Null(rows[0].RiskPct);
    }

    [Fact]
    public void Risco_alto_derruba_o_score_do_fornecedor_mais_barato()
    {
        var rows = MultiCriteriaScore.Compute(
        [
            new(A, "Barato arriscado", 1000m, 10, 30, 50, 90),
            new(B, "Confiável", 1050m, 10, 30, 100, 0),
        ]);

        Assert.Equal(B, rows[0].SupplierId);   // OTIF 100 + risco 0 superam 5% de preço
    }

    [Fact]
    public void Proposta_sem_valor_fica_fora()
    {
        var rows = MultiCriteriaScore.Compute([new(A, "Zerada", 0m, null, null, null, null)]);
        Assert.Empty(rows);
    }
}
