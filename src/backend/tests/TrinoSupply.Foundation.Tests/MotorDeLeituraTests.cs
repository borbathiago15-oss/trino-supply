using TrinoSupply.Foundation.Api.Acoes;
using TrinoSupply.Foundation.Api.Melhoria;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// A leitura automática (§3). Ela <b>não inventa nada</b>: só diz em português o que os campos
/// já afirmam — e onde falta dado, diz que falta, em vez de preencher com zero.
/// </summary>
public class MotorDeLeituraTests
{
    private static readonly DateOnly Hoje = new(2026, 9, 22);

    private static ImprovementCycle Ciclo(
        string fase = FaseDoCiclo.Do, decimal? baseline = null, decimal? meta = null,
        decimal? resultado = null, DateOnly? prazo = null, string? ferramenta = null,
        string? dados = null) => new()
        {
            Code = "PDCA-2026-001", Title = "Reduzir avarias na doca 2",
            Phase = fase, Indicator = "Avarias/mês", Unit = "un",
            Baseline = baseline, GoalValue = meta, ResultValue = resultado,
            GoalDeadline = prazo, StartDate = Hoje.AddDays(-10),
            ToolName = ferramenta, ToolData = dados,
            CreatedAt = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero),
        };

    private static ActionItem Acao(string status, DateOnly? prazo = null, string? causa = null) => new()
    {
        Number = "AC-2026-000001", Title = "Afixar o limite de empilhamento",
        Status = status, DueDate = prazo, RootCauseRef = causa,
    };

    private static LeituraDoCiclo Ler(ImprovementCycle c, params ActionItem[] acoes) =>
        MotorDeLeitura.Ler(c, FerramentaDeCausa.Normalizar(c.ToolName, c.ToolData), acoes, Hoje);

    // ---- indicador -----------------------------------------------------------

    [Fact]
    public void O_sentido_sai_da_meta_contra_a_linha_de_base()
    {
        Assert.Equal("reduzir", Ler(Ciclo(baseline: 40, meta: 10)).Indicador.Sentido);
        Assert.Equal("aumentar", Ler(Ciclo(baseline: 82, meta: 95)).Indicador.Sentido);
        Assert.Equal("indefinido", Ler(Ciclo()).Indicador.Sentido);
    }

    [Fact]
    public void Reduzir_atinge_quando_o_medido_fica_abaixo_da_meta()
    {
        Assert.True(Ler(Ciclo(baseline: 40, meta: 10, resultado: 8)).Indicador.AtingiuNoNumero);
        Assert.False(Ler(Ciclo(baseline: 40, meta: 10, resultado: 12)).Indicador.AtingiuNoNumero);
    }

    [Fact]
    public void Aumentar_atinge_quando_o_medido_passa_da_meta()
    {
        Assert.True(Ler(Ciclo(baseline: 82, meta: 95, resultado: 96)).Indicador.AtingiuNoNumero);
        Assert.False(Ler(Ciclo(baseline: 82, meta: 95, resultado: 90)).Indicador.AtingiuNoNumero);
    }

    [Fact]
    public void Sem_medicao_o_numero_nao_vira_veredito()
    {
        // "0% de avanço" e "ninguém mediu ainda" são notícias diferentes
        var l = Ler(Ciclo(baseline: 40, meta: 10));
        Assert.Null(l.Indicador.AtingiuNoNumero);
        Assert.Contains("ainda sem medição", l.Indicador.Frase);
    }

    // ---- prazo ---------------------------------------------------------------

    [Fact]
    public void Prazo_vencido_e_informacao_e_diz_que_nao_encerra()
    {
        var l = Ler(Ciclo(prazo: Hoje.AddDays(-5)));
        Assert.True(l.Prazo.Vencido);
        Assert.Equal(-5, l.Prazo.DiasRestantes);
        Assert.Contains("quem encerra é você", l.Prazo.Frase);
    }

    [Fact]
    public void O_percentual_de_tempo_consumido_conta_do_inicio_ao_prazo()
    {
        // começou há 10 dias, vence em 10: metade do tempo corrido
        var l = Ler(Ciclo(prazo: Hoje.AddDays(10)));
        Assert.Equal(50, l.Prazo.PercentualConsumido);
    }

    [Fact]
    public void Prazo_perto_do_fim_vira_sinal_antes_do_dia_do_vencimento()
    {
        Assert.Contains(Ler(Ciclo(prazo: Hoje.AddDays(3))).Sinais, s => s.Chave == "prazo-vencendo");
        Assert.DoesNotContain(Ler(Ciclo(prazo: Hoje.AddDays(30))).Sinais, s => s.Chave == "prazo-vencendo");
    }

    // ---- ações ---------------------------------------------------------------

    [Fact]
    public void Acao_suspensa_nao_conta_como_atrasada_e_continua_aberta()
    {
        // contá-la como atraso culparia a equipe por uma decisão da gestão
        var l = Ler(Ciclo(), Acao(StatusDaAcao.Suspensa, Hoje.AddDays(-20)));
        Assert.Equal(0, l.Acoes.Atrasadas);
        Assert.Equal(1, l.Acoes.Abertas);
        Assert.Equal(1, l.Acoes.Suspensas);
        Assert.DoesNotContain(l.Sinais, s => s.Chave == "acao-atrasada");
    }

    [Fact]
    public void Acao_pendente_vencida_conta_como_atrasada()
    {
        var l = Ler(Ciclo(), Acao(StatusDaAcao.Pendente, Hoje.AddDays(-1)));
        Assert.Equal(1, l.Acoes.Atrasadas);
        Assert.Contains(l.Sinais, s => s.Chave == "acao-atrasada");
    }

    // ---- causas e sinais -----------------------------------------------------

    [Fact]
    public void Causa_vital_sem_acao_acende_o_sinal()
    {
        var c = Ciclo(ferramenta: FerramentaDeCausa.Gut, dados: """
        {"itens":[{"problema":"Empilhamento acima do limite","g":5,"u":5,"t":5}]}
        """);
        var l = Ler(c);
        Assert.Contains(l.Sinais, s => s.Chave == "causa-vital-sem-acao");
        Assert.Equal(0, l.Causas[0].Acoes);
        Assert.Contains("Empilhamento", l.ProximoPasso);
    }

    [Fact]
    public void A_acao_que_aponta_a_causa_apaga_o_sinal()
    {
        var c = Ciclo(ferramenta: FerramentaDeCausa.Gut, dados: """
        {"itens":[{"problema":"Empilhamento acima do limite","g":5,"u":5,"t":5}]}
        """);
        var l = Ler(c, Acao(StatusDaAcao.Pendente, causa: "Empilhamento acima do limite"));
        Assert.DoesNotContain(l.Sinais, s => s.Chave == "causa-vital-sem-acao");
        Assert.Equal(1, l.Causas[0].Acoes);
    }

    [Fact]
    public void O_elo_entre_acao_e_causa_ignora_acento_caixa_e_pontuacao()
    {
        // exigir igualdade exata faria o sinal acender com a ação já escrita
        var c = Ciclo(ferramenta: FerramentaDeCausa.Gut, dados: """
        {"itens":[{"problema":"Falta de treinamento","g":5,"u":5,"t":5}]}
        """);
        var l = Ler(c, Acao(StatusDaAcao.Pendente, causa: "  falta de TREINAMENTO.  "));
        Assert.Equal(1, l.Causas[0].Acoes);
    }

    [Fact]
    public void Check_sem_medicao_acende_o_sinal()
    {
        var l = Ler(Ciclo(FaseDoCiclo.Check, baseline: 40, meta: 10));
        Assert.Contains(l.Sinais, s => s.Chave == "check-pendente");
        Assert.Contains("Meça o indicador", l.ProximoPasso);
    }

    [Fact]
    public void Ciclo_encerrado_com_acao_em_aberto_e_dito_na_leitura()
    {
        var l = Ler(Ciclo(FaseDoCiclo.Encerrado), Acao(StatusDaAcao.Pendente));
        Assert.Contains(l.Sinais, s => s.Chave == "encerrado-com-pendencia");
        Assert.Contains("em aberto", l.ProximoPasso);
    }

    [Fact]
    public void Ciclo_encerrado_nao_cobra_prazo()
    {
        // cobrar prazo de quem já fechou é barulho: a decisão já foi tomada
        var l = Ler(Ciclo(FaseDoCiclo.Encerrado, prazo: Hoje.AddDays(-30)));
        Assert.DoesNotContain(l.Sinais, s => s.Chave is "prazo-vencido" or "prazo-vencendo");
    }
}
