using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O caminho do processo na tela: quem pediu, de quem se espera agora, quem já decidiu e o
/// que falta. "Aguardando Aprovador 01" dizia a etapa e escondia tudo isso — e o sistema
/// sabia tudo isso.
/// </summary>
public class CaminhoDoProcessoTests
{
    private static readonly DateTimeOffset Dia1 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly AlcadasDoCentro Alcadas = new(["Bruno Gerente"], ["Dora Diretora", "Edu Diretor"]);

    private static Quotation Processo(QuotationStatus status) => new()
    {
        Status = status, CostCenter = "BAH-002", CreatedByLabel = "Carla Compradora", CreatedAt = Dia1,
        SelectedAt = status >= QuotationStatus.AwaitingManager ? Dia1.AddDays(2) : null,
        SelectedByLabel = status >= QuotationStatus.AwaitingManager ? "Carla Compradora" : null,
        ManagerApprovedAt = status >= QuotationStatus.AwaitingDirector ? Dia1.AddDays(3) : null,
        ManagerApprovedByLabel = status >= QuotationStatus.AwaitingDirector ? "Bruno Gerente" : null,
        DirectorApprovedAt = status >= QuotationStatus.ApprovedForIssue ? Dia1.AddDays(4) : null,
        DirectorApprovedByLabel = status >= QuotationStatus.ApprovedForIssue ? "Dora Diretora" : null,
    };

    private static EtapaDoCaminho Etapa(IReadOnlyList<EtapaDoCaminho> caminho, string chave) =>
        caminho.Single(e => e.Chave == chave);

    [Fact]
    public void Aguardando_nivel_1_diz_quem_pediu_quem_aprova_agora_e_quem_aprova_depois()
    {
        var caminho = CaminhoDoProcesso.De(Processo(QuotationStatus.AwaitingManager), ["Ana Solicitante"], Dia1.AddDays(-1), Alcadas);

        var pedido = Etapa(caminho, "solicitacao");
        Assert.Equal(CaminhoDoProcesso.Feita, pedido.Situacao);
        Assert.Equal("Ana Solicitante", pedido.Quem);
        Assert.Equal(Dia1.AddDays(-1), pedido.Em);

        Assert.Equal(CaminhoDoProcesso.Feita, Etapa(caminho, "cotacao").Situacao);
        var escolha = Etapa(caminho, "escolha");
        Assert.Equal(CaminhoDoProcesso.Feita, escolha.Situacao);
        Assert.Equal("Carla Compradora", escolha.Quem);

        // a etapa atual diz o nome de quem tem a bola, e desde quando (a escolha do vencedor)
        var n1 = Etapa(caminho, "nivel1");
        Assert.Equal(CaminhoDoProcesso.Atual, n1.Situacao);
        Assert.Equal("Bruno Gerente", n1.Quem);
        Assert.Equal(Dia1.AddDays(2), n1.Em);

        // a pendente também diz quem vai aprovar — dá para avisar antes de a bola chegar
        var n2 = Etapa(caminho, "nivel2");
        Assert.Equal(CaminhoDoProcesso.Pendente, n2.Situacao);
        Assert.Equal("Dora Diretora, Edu Diretor", n2.Quem);
        Assert.Null(n2.Em);

        Assert.Equal(CaminhoDoProcesso.Pendente, Etapa(caminho, "oc").Situacao);
        Assert.Equal(CaminhoDoProcesso.Pendente, Etapa(caminho, "entrega").Situacao);
        Assert.Single(caminho, e => e.Situacao == CaminhoDoProcesso.Atual);
    }

    [Fact]
    public void Centro_sem_aprovador_cadastrado_diz_isso_na_linha()
    {
        var caminho = CaminhoDoProcesso.De(Processo(QuotationStatus.AwaitingManager), ["Ana"], Dia1, AlcadasDoCentro.Nenhuma);

        Assert.Equal("sem aprovador cadastrado no centro", Etapa(caminho, "nivel1").Quem);
        // a marca é o que a tela usa para oferecer o link do cadastro — texto não é contrato
        Assert.True(Etapa(caminho, "nivel1").SemAprovador);
        Assert.True(Etapa(caminho, "nivel2").SemAprovador);
    }

    [Fact]
    public void Com_aprovador_cadastrado_a_marca_fica_desligada_mesmo_na_etapa_ja_feita()
    {
        var caminho = CaminhoDoProcesso.De(Processo(QuotationStatus.AwaitingDirector), ["Ana"], Dia1, Alcadas);

        Assert.False(Etapa(caminho, "nivel1").SemAprovador);
        Assert.False(Etapa(caminho, "nivel2").SemAprovador);
    }

    [Fact]
    public void Nivel_1_dado_vira_feito_por_quem_e_quando_e_o_nivel_2_passa_a_esperar()
    {
        var caminho = CaminhoDoProcesso.De(Processo(QuotationStatus.AwaitingDirector), ["Ana"], Dia1, Alcadas);

        var n1 = Etapa(caminho, "nivel1");
        Assert.Equal(CaminhoDoProcesso.Feita, n1.Situacao);
        Assert.Equal("Bruno Gerente", n1.Quem);
        Assert.Equal(Dia1.AddDays(3), n1.Em);

        var n2 = Etapa(caminho, "nivel2");
        Assert.Equal(CaminhoDoProcesso.Atual, n2.Situacao);
        Assert.Equal(Dia1.AddDays(3), n2.Em);   // o relógio do Nível 2 é a aprovação do Nível 1
    }

    [Fact]
    public void Aprovado_para_emissao_espera_o_comprador_registrar_a_OC()
    {
        var caminho = CaminhoDoProcesso.De(Processo(QuotationStatus.ApprovedForIssue), ["Ana"], Dia1, Alcadas);

        var oc = Etapa(caminho, "oc");
        Assert.Equal(CaminhoDoProcesso.Atual, oc.Situacao);
        Assert.Equal("Carla Compradora", oc.Quem);
        Assert.Equal(Dia1.AddDays(4), oc.Em);
    }

    [Fact]
    public void OC_registrada_fecha_a_compra_e_passa_a_bola_para_a_entrega()
    {
        var caminho = CaminhoDoProcesso.De(Processo(QuotationStatus.PoIssued), ["Ana"], Dia1, Alcadas);

        Assert.Equal(CaminhoDoProcesso.Feita, Etapa(caminho, "oc").Situacao);
        Assert.Equal(CaminhoDoProcesso.Atual, Etapa(caminho, "entrega").Situacao);
    }

    [Fact]
    public void Processo_rejeitado_nao_espera_ninguem_e_termina_dizendo_isso()
    {
        var q = Processo(QuotationStatus.AwaitingManager);
        q.Status = QuotationStatus.Rejected;

        var caminho = CaminhoDoProcesso.De(q, ["Ana"], Dia1, Alcadas);

        Assert.DoesNotContain(caminho, e => e.Situacao == CaminhoDoProcesso.Atual);
        Assert.Equal(CaminhoDoProcesso.Feita, Etapa(caminho, "escolha").Situacao);   // o que aconteceu, ficou
        var fim = caminho[^1];
        Assert.Equal("encerrado", fim.Chave);
        Assert.Equal("Rejeitado", fim.Titulo);
        Assert.Equal(CaminhoDoProcesso.Encerrada, fim.Situacao);
    }

    [Fact]
    public void Solicitantes_repetidos_aparecem_uma_vez_e_sem_SC_a_linha_fica_sem_nome()
    {
        var q = Processo(QuotationStatus.Open);
        Assert.Equal("Ana, Beto", Etapa(CaminhoDoProcesso.De(q, ["Ana", "Beto", "Ana"], Dia1, Alcadas), "solicitacao").Quem);
        Assert.Null(Etapa(CaminhoDoProcesso.De(q, [], null, Alcadas), "solicitacao").Quem);
    }
}
