using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O commit que o <c>/health</c> publica. Entrega que só mexe no servidor não muda o
/// bundle do navegador — sem este campo não havia como, de fora, distinguir "o deploy
/// subiu" de "o deploy falhou e o anterior continua rodando".
/// </summary>
public class VersaoImplantadaTests
{
    private static Func<string, string?> Ambiente(params (string chave, string? valor)[] vars) =>
        nome => vars.FirstOrDefault(v => v.chave == nome).valor;

    [Fact]
    public void Commit_vem_do_Railway_e_o_manual_e_a_saida_de_quem_sobe_a_imagem_por_fora()
    {
        var doRailway = Ambiente(
            (VersaoImplantada.VariavelDoRailway, "5844a78e40bc06636aff2ceeff24f2e4f2f77a10"),
            (VersaoImplantada.VariavelManual, "manual"));
        // a implantação real manda; GIT_SHA existe para quem constrói a imagem na mão
        Assert.Equal("5844a78e40bc06636aff2ceeff24f2e4f2f77a10", VersaoImplantada.Commit(doRailway));

        var soManual = Ambiente((VersaoImplantada.VariavelManual, "abc1234"));
        Assert.Equal("abc1234", VersaoImplantada.Commit(soManual));
    }

    [Fact]
    public void Sem_variavel_nenhuma_o_commit_e_nulo_em_vez_de_inventado()
    {
        // nulo é resposta: quem verifica fica sabendo que não sabe. Um "desconhecido"
        // ou a versão do assembly dariam a impressão de resposta a quem está
        // justamente conferindo se o deploy subiu
        Assert.Null(VersaoImplantada.Commit(Ambiente()));
        // e variável presente porém vazia é o mesmo que ausente
        Assert.Null(VersaoImplantada.Commit(Ambiente(
            (VersaoImplantada.VariavelDoRailway, "   "), (VersaoImplantada.VariavelManual, ""))));
    }

    [Fact]
    public void Commit_curto_e_o_que_se_compara_de_olho_com_a_lista_do_GitHub()
    {
        Assert.Equal("5844a78", VersaoImplantada.Curto("5844a78e40bc06636aff2ceeff24f2e4f2f77a10"));
        // sha mais curto que sete não é cortado ao meio nem estoura
        Assert.Equal("abc", VersaoImplantada.Curto("abc"));
        Assert.Null(VersaoImplantada.Curto(null));
    }
}
