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

    [Fact]
    public void Os_pesos_publicados_somam_cem_por_cento()
    {
        // a tela mostra "Preço 40% · Entrega 20% · …" como se fosse a repartição de um todo;
        // se a soma não fechasse, a explicação estaria mentindo sobre a própria conta
        Assert.Equal(1.0, MultiCriteriaScore.Criterios.Sum(c => c.Weight), 6);
        Assert.Equal(5, MultiCriteriaScore.Criterios.Select(c => c.Code).Distinct().Count());
    }

    [Fact]
    public void O_peso_usado_na_conta_e_o_mesmo_que_a_tela_recebe()
    {
        // preço 40 e OTIF 20: com preço 100 num e OTIF 100 no outro, quem leva o preço ganha
        var preco = MultiCriteriaScore.Criterios.Single(c => c.Code == "price").Weight;
        var otif = MultiCriteriaScore.Criterios.Single(c => c.Code == "otif").Weight;

        var rows = MultiCriteriaScore.Compute(
        [
            new(A, "Melhor preço", 1000m, null, null, 0, null),
            new(B, "Melhor OTIF", 2000m, null, null, 100, null),
        ]);

        // A: preço 100 e OTIF 0 -> 100*0,4 / (0,4+0,2); B: preço 50 e OTIF 100
        Assert.Equal(Math.Round(100 * preco / (preco + otif), 1), rows.Single(r => r.SupplierId == A).Score);
        Assert.Equal(Math.Round((50 * preco + 100 * otif) / (preco + otif), 1),
            rows.Single(r => r.SupplierId == B).Score);
    }
}
