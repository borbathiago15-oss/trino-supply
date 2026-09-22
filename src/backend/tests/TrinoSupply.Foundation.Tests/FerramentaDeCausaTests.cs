using TrinoSupply.Foundation.Api.Melhoria;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// As cinco ferramentas de análise de causa (§2 da especificação).
///
/// <para>
/// As duas regras com história de defeito estão aqui: o <b>Pareto</b> marca como vital o item
/// que <i>cruza</i> os 80% (usar o acumulado depois dele o deixaria justamente de fora), e o
/// <b>GUT</b> não elege ninguém quando o maior produto fica abaixo de 27.
/// </para>
/// </summary>
public class FerramentaDeCausaTests
{
    private static AnaliseDeCausa Ler(string nome, string json) =>
        FerramentaDeCausa.Normalizar(nome, json)!;

    // ---- Pareto --------------------------------------------------------------

    [Fact]
    public void Pareto_marca_como_vital_o_item_que_cruza_os_80_por_cento()
    {
        // 50 / 25 / 15 / 10 → acumulados 50, 75, 90, 100.
        // O terceiro item CRUZA os 80%: o acumulado ANTES dele é 75 (< 80), então ele é vital.
        // Fosse pelo acumulado depois (90), ele ficaria de fora e a conta nunca fecharia 80%.
        var a = Ler(FerramentaDeCausa.Pareto, """
        {"itens":[{"causa":"Embalagem","valor":15},{"causa":"Manuseio","valor":50},
                  {"causa":"Empilhamento","valor":25},{"causa":"Transporte","valor":10}]}
        """);

        Assert.Equal(["Manuseio", "Empilhamento", "Embalagem", "Transporte"],
            a.Causas.Select(c => c.Rotulo));
        Assert.Equal([true, true, true, false], a.Causas.Select(c => c.Vital));
        Assert.Equal(90m, a.Causas[2].Acumulado);
    }

    [Fact]
    public void Pareto_ordena_por_valor_decrescente_e_calcula_percentual_e_acumulado()
    {
        var a = Ler(FerramentaDeCausa.Pareto, """
        {"itens":[{"causa":"B","valor":30},{"causa":"A","valor":70}]}
        """);
        Assert.Equal("A", a.Causas[0].Rotulo);
        Assert.Equal(70m, a.Causas[0].Percentual);
        Assert.Equal(70m, a.Causas[0].Acumulado);
        Assert.Equal(100m, a.Causas[1].Acumulado);
    }

    [Fact]
    public void Pareto_com_um_item_so_o_deixa_vital()
    {
        var a = Ler(FerramentaDeCausa.Pareto, """{"itens":[{"causa":"Única","valor":9}]}""");
        Assert.True(Assert.Single(a.Causas).Vital);
    }

    // ---- GUT -----------------------------------------------------------------

    [Fact]
    public void Gut_elege_o_maior_produto_quando_ele_chega_a_27()
    {
        var a = Ler(FerramentaDeCausa.Gut, """
        {"itens":[{"problema":"Fila na doca","g":3,"u":3,"t":3},
                  {"problema":"Avaria no palete","g":5,"u":4,"t":3}]}
        """);
        Assert.Equal("Avaria no palete", a.Causas[0].Rotulo);
        Assert.Equal(60m, a.Causas[0].Valor);
        Assert.True(a.Causas[0].Vital);
        Assert.False(a.Causas[1].Vital);
    }

    [Fact]
    public void Gut_nao_elege_ninguem_quando_o_maior_produto_fica_abaixo_de_27()
    {
        // 2·2·2 = 8 é o maior de uma lista fraca, não uma prioridade. Marcá-lo mandaria a
        // equipe atacar o que a própria matriz diz que pode esperar.
        var a = Ler(FerramentaDeCausa.Gut, """
        {"itens":[{"problema":"Etiqueta torta","g":2,"u":2,"t":2},
                  {"problema":"Papel acabando","g":1,"u":2,"t":3}]}
        """);
        Assert.Equal(2, a.Causas.Count);
        Assert.All(a.Causas, c => Assert.False(c.Vital));
        Assert.Empty(a.Vitais);
    }

    [Fact]
    public void Gut_no_limite_exato_de_27_elege()
    {
        var a = Ler(FerramentaDeCausa.Gut, """{"itens":[{"problema":"X","g":3,"u":3,"t":3}]}""");
        Assert.True(Assert.Single(a.Causas).Vital);
    }

    [Fact]
    public void Gut_ignora_nota_fora_da_faixa_de_1_a_5()
    {
        var a = Ler(FerramentaDeCausa.Gut, """
        {"itens":[{"problema":"Bom","g":5,"u":5,"t":5},{"problema":"Nota inválida","g":9,"u":9,"t":9}]}
        """);
        Assert.Equal("Bom", Assert.Single(a.Causas).Rotulo);
    }

    // ---- 5 Porquês -----------------------------------------------------------

    [Fact]
    public void Cinco_porques_tem_a_causa_raiz_sempre_vital()
    {
        var a = Ler(FerramentaDeCausa.CincoPorques, """
        {"problema":"Avarias na doca","porque1":"Por que houve avaria?","resposta1":"Palete caiu",
         "porque2":"Por que caiu?","resposta2":"Empilhado acima do limite",
         "causa_raiz":"Não há limite de empilhamento afixado"}
        """);
        Assert.Equal(2, a.Degraus.Count);
        Assert.Equal("Não há limite de empilhamento afixado", a.CausaRaiz);
        Assert.True(Assert.Single(a.Causas).Vital);
    }

    // ---- Ishikawa e Brainstorming --------------------------------------------

    [Fact]
    public void Ishikawa_levanta_mas_nao_prioriza()
    {
        var a = Ler(FerramentaDeCausa.Ishikawa, """
        {"efeito":"Avarias","maquina":["Empilhadeira sem manutenção"],
         "mao_de_obra":["Sem treinamento","Rotatividade alta"]}
        """);
        Assert.Equal("Avarias", a.Efeito);
        Assert.Equal(6, a.Grupos.Count);
        Assert.Equal(3, a.Causas.Count);
        Assert.All(a.Causas, c => Assert.False(c.Vital));
        Assert.Equal("Mão de obra", a.Causas[1].Detalhe);
    }

    [Fact]
    public void Brainstorming_guarda_as_ideias_e_nao_elege_nenhuma()
    {
        var a = Ler(FerramentaDeCausa.Brainstorming, """{"ideias":["Trocar o filme","Rever a rota"]}""");
        Assert.Equal(2, a.Ideias.Count);
        Assert.Empty(a.Vitais);
    }

    // ---- as bordas -----------------------------------------------------------

    [Fact]
    public void Ferramenta_nao_escolhida_nao_vira_analise_vazia()
    {
        Assert.Null(FerramentaDeCausa.Normalizar(null, """{"itens":[]}"""));
        Assert.Null(FerramentaDeCausa.Normalizar("QUALQUER_COISA", "{}"));
    }

    [Fact]
    public void Json_ilegivel_volta_como_aviso_em_vez_de_derrubar_o_ciclo()
    {
        var a = Ler(FerramentaDeCausa.Pareto, "{isto não é json");
        Assert.NotNull(a.Aviso);
        Assert.Empty(a.Causas);
    }

    // ---- as três que faltavam para igualar o Trino Intelligence ---------------

    [Fact]
    public void O_5W2H_organiza_a_execucao_e_nao_elege_causa()
    {
        // é o oposto de priorizar: a causa já está clara, e aqui se organiza o como
        var a = Ler(FerramentaDeCausa.CincoWDoisH, """
        {"linhas":[{"oque":"Afixar o limite","porque":"Ninguém sabe o teto","onde":"Doca 2",
                    "quando":"10/10","quem":"Ana","como":"Cartaz A3","quanto":"R$ 120"}]}
        """);
        var linha = Assert.Single(a.Linhas!);
        Assert.Equal("Afixar o limite", linha.OQue);
        Assert.Equal("Ana", linha.Quem);
        Assert.Empty(a.Vitais);
    }

    [Fact]
    public void O_5W2H_aceita_o_formato_do_Trino_Intelligence()
    {
        var a = Ler(FerramentaDeCausa.CincoWDoisH,
            """{"rows":[{"what":"Treinar a doca","who":"Bruno","how_much":"R$ 0"}]}""");
        var linha = Assert.Single(a.Linhas!);
        Assert.Equal("Treinar a doca", linha.OQue);
        Assert.Equal("R$ 0", linha.Quanto);
    }

    [Fact]
    public void O_Kaizen_guarda_o_antes_o_depois_e_o_resultado()
    {
        var a = Ler(FerramentaDeCausa.Kaizen, """
        {"antes":"Palete solto","depois":"Palete cintado",
         "melhorias":["Cinta plástica","Cartaz"],"resultados":"Zero avaria em 30 dias"}
        """);
        Assert.Equal("Palete solto", a.Antes);
        Assert.Equal("Palete cintado", a.Depois);
        Assert.Equal(2, a.Ideias.Count);
        Assert.Equal("Zero avaria em 30 dias", a.Resultado);
        Assert.Empty(a.Vitais);
    }

    [Fact]
    public void O_Fluxograma_poe_o_atual_ao_lado_do_proposto()
    {
        // a comparação é a informação: um fluxo sozinho não diz o que muda
        var a = Ler(FerramentaDeCausa.Fluxograma,
            """{"atual":["Recebe","Empilha"],"proposto":["Recebe","Confere","Empilha"]}""");
        Assert.Equal(["Recebe", "Empilha"], a.FluxoAtual);
        Assert.Equal(["Recebe", "Confere", "Empilha"], a.FluxoProposto);
    }

    [Fact]
    public void O_fluxograma_aceita_a_lista_de_objetos_do_Trino_Intelligence()
    {
        var a = Ler(FerramentaDeCausa.Fluxograma,
            """{"current":[{"step":"Recebe"}],"proposed":[{"step":"Confere"}]}""");
        Assert.Equal(["Recebe"], a.FluxoAtual);
        Assert.Equal(["Confere"], a.FluxoProposto);
    }

    [Fact]
    public void As_oito_ferramentas_tem_nome_e_para_que_serve()
    {
        // a ferramenta errada para o problema é o que faz a análise virar formulário
        // preenchido sem serventia — a tela mostra para que serve na hora de escolher
        Assert.Equal(8, FerramentaDeCausa.Todas.Length);
        foreach (var t in FerramentaDeCausa.Todas)
        {
            Assert.NotEqual(t, FerramentaDeCausa.Rotulo(t));
            Assert.NotEmpty(FerramentaDeCausa.ParaQue(t));
        }
    }
}
