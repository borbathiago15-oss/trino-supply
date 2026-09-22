using TrinoSupply.Foundation.Api.Catalog;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// A convenção da grade: como o tamanho entra no código e na descrição, e em que ordem os
/// tamanhos aparecem. Ordem alfabética daria "G, GG, M, P" — que não é grade nenhuma.
/// </summary>
public class TamanhosTests
{
    [Fact]
    public void A_grade_sai_na_ordem_de_vestuario_e_nao_na_ordem_em_que_foi_digitada()
    {
        Assert.Equal(["PP", "P", "M", "G", "GG"], Tamanhos.Normalizar("M, GG,p , pp,G"));
        Assert.Equal(["38", "39", "40", "41"], Tamanhos.Normalizar("40;39/41\n38"));
        // repetido entra uma vez só, e espaço sobrando não vira tamanho novo
        Assert.Equal(["P", "M"], Tamanhos.Normalizar(" p , P ,m "));
        Assert.Empty(Tamanhos.Normalizar("  "));
        Assert.Empty(Tamanhos.Normalizar(null));
    }

    [Fact]
    public void Tamanho_desconhecido_vai_para_o_fim_em_vez_de_sumir()
    {
        // "ÚNICO" não é letra da grade nem número: continua cadastrável, só não se mete no meio
        Assert.Equal(["P", "G", "42", "ÚNICO"], Tamanhos.Normalizar("ÚNICO, 42, G, P"));
        Assert.True(Tamanhos.Ordem("ÚNICO") > Tamanhos.Ordem("46"));
        Assert.True(Tamanhos.Ordem("46") > Tamanhos.Ordem("XXG"));
        // produto sem tamanho vem antes de qualquer variante
        Assert.True(Tamanhos.Ordem(null) < Tamanhos.Ordem("PP"));
    }

    [Fact]
    public void Codigo_e_descricao_da_variante_seguem_a_mesma_convencao_da_importacao()
    {
        Assert.Equal("12003-P", Tamanhos.CodigoDaVariante("12003", "P"));
        Assert.Equal("12003", Tamanhos.CodigoDaVariante("12003", null));
        Assert.Equal("Bota de segurança — Tam. 40", Tamanhos.DescricaoDaVariante("Bota de segurança", "40"));
        Assert.Equal("Bota de segurança", Tamanhos.DescricaoDaVariante("Bota de segurança", null));
    }

    [Fact]
    public void A_descricao_base_tira_o_tamanho_para_o_seletor_mostrar_o_produto_uma_vez()
    {
        Assert.Equal("Bota de segurança", Tamanhos.DescricaoBase("Bota de segurança — Tam. 40", "40"));
        // descrição que não segue a convenção fica como está: derivar não pode inventar
        Assert.Equal("Bota 40 cano longo", Tamanhos.DescricaoBase("Bota 40 cano longo", "40"));
        Assert.Equal("Detergente", Tamanhos.DescricaoBase("Detergente", null));
    }
}
