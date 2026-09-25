using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O prazo de resposta é <b>de cada convite</b>, e o comprador tem duas saídas quando ele
/// vence: mais tempo, ou seguir sem o fornecedor.
///
/// O que estes testes protegem é o que faria a cobrança ser injusta ou a história do processo
/// ficar falsa: prazo único cobrando do último a folga do primeiro, "novo prazo" que só apaga
/// o aviso sem reabrir a porta, e convite dispensado sumindo do processo — que transformaria
/// "chamei três e um não veio" em "só chamei dois".
/// </summary>
public class PrazoDoConviteTests
{
    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = agora;
        public override DateTimeOffset GetUtcNow() => Agora;
    }

    private sealed class NumerosFalsos : IPrNumberGenerator
    {
        private int _proximo;
        public Task<string> NextAsync(CancellationToken ct = default) =>
            Task.FromResult($"PR-2026-{++_proximo:000000}");
    }

    private static readonly Actor Carla = new(Guid.NewGuid(), "Carla", Roles.PurchasingOfficer);
    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana", Roles.Requester);
    private static readonly Actor Bruno = new(Guid.NewGuid(), "Bruno", Roles.Approver);
    private static readonly DateTimeOffset Segunda = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private sealed record Mundo(
        QuotationService Rfq, AppDbContext Db, RelogioFixo Relogio,
        Supplier Alfa, Supplier Beta, Quotation Q);

    private static async Task<Mundo> BuildAsync(DateOnly? prazoDoProcesso = null)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(new ItensDaCotacaoNoCatalogo()).Options);
        var relogio = new RelogioFixo(Segunda);
        var rfq = new QuotationService(db, relogio);
        var sup = new SupplierService(db, relogio);
        var prs = new RequisitionService(db, new NumerosFalsos(), new CatalogService(db, relogio), relogio);

        var (alfa, _) = await sup.CreateAsync(Carla.Id, "Alfa LTDA", "Alfa", "12345678000190", null, "81 3333-1000");
        var (beta, _) = await sup.CreateAsync(Carla.Id, "Beta LTDA", "Beta", "98765432000110", null, "81 3333-2000");
        await sup.SetHomologationAsync(alfa!.Id, SupplierHomologation.Homologado);
        await sup.SetHomologationAsync(beta!.Id, SupplierHomologation.Homologado);

        var (pr, _) = await prs.CreateAsync(Ana, "Ferramentas", "CC-01", "NORMAL", null,
            [new ItemInput("Martelete", 1, "UN", 900, null)]);
        await prs.SubmitAsync(Ana, pr!.Id);
        await prs.ApproveAsync(Bruno, pr.Id, null);
        var (q, _) = await rfq.CreateFromPrAsync(Carla, pr.Id, QuotationKind.Purchase, prazoDoProcesso, null);
        return new Mundo(rfq, db, relogio, alfa, beta!, q!);
    }

    private static ProposalInput Proposta(Quotation q, decimal preco) =>
        new(15, "28 dias", 0, null, null,
            [new ProposalItemInput(q.Items.Single().Id, preco, null)]);

    private static DateOnly Dia(int dia) => new(2026, 9, dia);

    [Fact]
    public async Task O_prazo_e_de_cada_convite_e_nao_do_processo()
    {
        // convidar na segunda e na quinta com um prazo só cobraria do segundo a folga
        // que se deu ao primeiro
        var w = await BuildAsync();
        await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [w.Alfa.Id], default, Dia(11));
        await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [w.Beta.Id], default, Dia(18));

        var atual = await w.Rfq.GetAsync(w.Q.Id);
        var convites = QuotationService.SituacaoDosConvites(atual!, Dia(7));

        Assert.Equal(Dia(11), convites.Single(c => c.SupplierId == w.Alfa.Id).Deadline);
        Assert.Equal(Dia(18), convites.Single(c => c.SupplierId == w.Beta.Id).Deadline);
    }

    [Fact]
    public async Task Sem_prazo_proprio_vale_o_do_processo()
    {
        // é o que mantém legível todo convite feito antes desta regra existir
        var w = await BuildAsync(prazoDoProcesso: Dia(15));
        await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [w.Alfa.Id]);

        var atual = await w.Rfq.GetAsync(w.Q.Id);
        Assert.Equal(Dia(15), QuotationService.SituacaoDosConvites(atual!, Dia(7)).Single().Deadline);
    }

    [Fact]
    public async Task Convite_sem_prazo_nenhum_nao_tem_atraso_a_mostrar()
    {
        // "0 dias de atraso" sobre convite sem data seria afirmar pontualidade não combinada
        var w = await BuildAsync();
        await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [w.Alfa.Id]);

        var atual = await w.Rfq.GetAsync(w.Q.Id);
        var convite = QuotationService.SituacaoDosConvites(atual!, Dia(30)).Single();

        Assert.Null(convite.DaysLate);
        Assert.False(convite.Late);
        Assert.True(convite.Pending);
    }

    [Fact]
    public async Task Passado_o_prazo_sem_proposta_o_convite_aparece_atrasado()
    {
        var w = await BuildAsync();
        await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [w.Alfa.Id], default, Dia(9));

        var atual = await w.Rfq.GetAsync(w.Q.Id);
        var convite = QuotationService.SituacaoDosConvites(atual!, Dia(12)).Single();

        Assert.Equal(3, convite.DaysLate);
        Assert.True(convite.Late);
    }

    [Fact]
    public async Task Quem_respondeu_nao_esta_atrasado_mesmo_com_prazo_vencido()
    {
        var w = await BuildAsync();
        await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [w.Alfa.Id], default, Dia(9));
        var (_, erro) = await w.Rfq.SubmitProposalAsync(w.Q.Id, w.Alfa.Id, Proposta(w.Q, 800m), "PORTAL", "Alfa");
        Assert.Null(erro);

        var atual = await w.Rfq.GetAsync(w.Q.Id);
        var convite = QuotationService.SituacaoDosConvites(atual!, Dia(30)).Single();

        Assert.True(convite.Responded);
        Assert.False(convite.Late);
        Assert.False(convite.Pending);
    }

    [Fact]
    public async Task Prazo_vencido_barra_a_proposta_e_o_novo_prazo_reabre()
    {
        // é o que faz "dar novo prazo" ter efeito de verdade, e não só apagar o aviso da tela
        var w = await BuildAsync();
        await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [w.Alfa.Id], default, Dia(9));
        w.Relogio.Agora = new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

        var (_, barrada) = await w.Rfq.SubmitProposalAsync(w.Q.Id, w.Alfa.Id, Proposta(w.Q, 800m), "PORTAL", "Alfa");
        Assert.Equal("RFQ-ERR-020", barrada!.Code);

        var (_, erroPrazo) = await w.Rfq.ProrrogarConviteAsync(Carla, w.Q.Id, w.Alfa.Id, Dia(20));
        Assert.Null(erroPrazo);

        var (proposta, aceita) = await w.Rfq.SubmitProposalAsync(w.Q.Id, w.Alfa.Id, Proposta(w.Q, 800m), "PORTAL", "Alfa");
        Assert.Null(aceita);
        Assert.NotNull(proposta);
    }

    [Fact]
    public async Task O_prazo_do_outro_fornecedor_nao_se_move_junto()
    {
        var w = await BuildAsync();
        await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [w.Alfa.Id, w.Beta.Id], default, Dia(9));
        await w.Rfq.ProrrogarConviteAsync(Carla, w.Q.Id, w.Alfa.Id, Dia(20));

        var atual = await w.Rfq.GetAsync(w.Q.Id);
        var convites = QuotationService.SituacaoDosConvites(atual!, Dia(7));

        Assert.Equal(Dia(20), convites.Single(c => c.SupplierId == w.Alfa.Id).Deadline);
        Assert.Equal(Dia(9), convites.Single(c => c.SupplierId == w.Beta.Id).Deadline);
    }

    [Fact]
    public async Task Prorrogar_conta_quantas_vezes_e_recusa_prazo_que_encurta()
    {
        // folga repetida é fato sobre o fornecedor, e some se cada data apagar a anterior
        var w = await BuildAsync();
        await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [w.Alfa.Id], default, Dia(9));
        await w.Rfq.ProrrogarConviteAsync(Carla, w.Q.Id, w.Alfa.Id, Dia(15));
        await w.Rfq.ProrrogarConviteAsync(Carla, w.Q.Id, w.Alfa.Id, Dia(20));

        var (_, encurta) = await w.Rfq.ProrrogarConviteAsync(Carla, w.Q.Id, w.Alfa.Id, Dia(18));
        Assert.Equal("RFQ-ERR-071", encurta!.Code);

        var (_, passado) = await w.Rfq.ProrrogarConviteAsync(Carla, w.Q.Id, w.Alfa.Id, Dia(1));
        Assert.Equal("RFQ-ERR-071", passado!.Code);

        var atual = await w.Rfq.GetAsync(w.Q.Id);
        Assert.Equal(2, QuotationService.SituacaoDosConvites(atual!, Dia(7)).Single().Extensions);
    }

    [Fact]
    public async Task Seguir_sem_o_fornecedor_nao_apaga_o_convite()
    {
        // "chamei três e um não veio" é uma história diferente de "só chamei dois", e é
        // ela que explica um BID com menos proponentes
        var w = await BuildAsync();
        await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [w.Alfa.Id, w.Beta.Id], default, Dia(9));

        var (_, erro) = await w.Rfq.DispensarConviteAsync(
            Carla, w.Q.Id, w.Beta.Id, "Nao respondeu apos tres cobrancas por telefone");
        Assert.Null(erro);

        var atual = await w.Rfq.GetAsync(w.Q.Id);
        var convites = QuotationService.SituacaoDosConvites(atual!, Dia(12));
        var beta = convites.Single(c => c.SupplierId == w.Beta.Id);

        Assert.Equal(2, convites.Count);
        Assert.True(beta.Waived);
        Assert.False(beta.Pending);
        Assert.False(beta.Late);   // dispensado deixa de cobrar: a decisão já foi tomada
        Assert.Contains("tres cobrancas", beta.WaivedReason);
    }

    [Fact]
    public async Task Dispensa_exige_motivo_e_o_dispensado_nao_propoe_mais()
    {
        var w = await BuildAsync();
        await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [w.Alfa.Id], default, Dia(20));

        var (_, curto) = await w.Rfq.DispensarConviteAsync(Carla, w.Q.Id, w.Alfa.Id, "sumiu");
        Assert.Equal("RFQ-ERR-072", curto!.Code);

        await w.Rfq.DispensarConviteAsync(Carla, w.Q.Id, w.Alfa.Id, "Fornecedor avisou que nao vai cotar");
        var (_, recusada) = await w.Rfq.SubmitProposalAsync(w.Q.Id, w.Alfa.Id, Proposta(w.Q, 800m), "PORTAL", "Alfa");
        Assert.Equal("RFQ-ERR-050", recusada!.Code);
    }

    [Fact]
    public async Task Nao_se_dispensa_quem_ja_respondeu()
    {
        // proposta na mesa é o oposto de ausência; recusá-la é decisão de adjudicação
        var w = await BuildAsync();
        await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [w.Alfa.Id], default, Dia(20));
        await w.Rfq.SubmitProposalAsync(w.Q.Id, w.Alfa.Id, Proposta(w.Q, 800m), "PORTAL", "Alfa");

        var (_, erro) = await w.Rfq.DispensarConviteAsync(
            Carla, w.Q.Id, w.Alfa.Id, "Achei o preco alto demais para seguir");

        Assert.Equal("RFQ-ERR-072", erro!.Code);
        Assert.Contains("adjudicação", erro.Message);
    }

    [Fact]
    public async Task Reconvidar_o_dispensado_reabre_o_convite()
    {
        // dispensar por engano não pode deixar o fornecedor fora do processo para sempre
        var w = await BuildAsync();
        await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [w.Alfa.Id], default, Dia(9));
        await w.Rfq.DispensarConviteAsync(Carla, w.Q.Id, w.Alfa.Id, "Dispensado por engano nesta linha");

        var (_, erro) = await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [w.Alfa.Id], default, Dia(20));
        Assert.Null(erro);

        var atual = await w.Rfq.GetAsync(w.Q.Id);
        var convite = QuotationService.SituacaoDosConvites(atual!, Dia(12)).Single();

        Assert.False(convite.Waived);
        Assert.Equal(Dia(20), convite.Deadline);
        Assert.Single(atual!.Suppliers);   // reabriu o convite, não criou um segundo
    }

    [Fact]
    public async Task Fora_da_fase_de_cotacao_o_prazo_nao_muda_mais()
    {
        var w = await BuildAsync();
        await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [w.Alfa.Id], default, Dia(20));
        await w.Rfq.SubmitProposalAsync(w.Q.Id, w.Alfa.Id, Proposta(w.Q, 800m), "PORTAL", "Alfa");
        await w.Rfq.CloseForAnalysisAsync(Carla, w.Q.Id);
        var atual = await w.Rfq.GetAsync(w.Q.Id);
        await w.Rfq.AwardByItemAsync(Carla, w.Q.Id,
            [new AwardInput("", atual!.Proposals.Single().Id, "Menor preco", "Unica proposta recebida",
                atual.Items.Single().Id)]);

        var (_, erro) = await w.Rfq.ProrrogarConviteAsync(Carla, w.Q.Id, w.Alfa.Id, Dia(30));
        Assert.Equal("RFQ-ERR-070", erro!.Code);
    }

    [Fact]
    public async Task Quem_nao_conduz_compras_nao_mexe_no_prazo()
    {
        var w = await BuildAsync();
        await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [w.Alfa.Id], default, Dia(20));

        var (_, prazo) = await w.Rfq.ProrrogarConviteAsync(Ana, w.Q.Id, w.Alfa.Id, Dia(30));
        var (_, dispensa) = await w.Rfq.DispensarConviteAsync(Ana, w.Q.Id, w.Alfa.Id, "Nao respondeu nada ate agora");

        Assert.Equal("RFQ-ERR-900", prazo!.Code);
        Assert.Equal("RFQ-ERR-900", dispensa!.Code);
    }

    [Fact]
    public async Task O_atrasado_aparece_primeiro_na_lista()
    {
        // a lista é para cobrar: quem está devendo não pode estar no fim dela
        var w = await BuildAsync();
        await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [w.Alfa.Id], default, Dia(30));
        await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [w.Beta.Id], default, Dia(9));

        var atual = await w.Rfq.GetAsync(w.Q.Id);
        var convites = QuotationService.SituacaoDosConvites(atual!, Dia(12));

        Assert.Equal(w.Beta.Id, convites[0].SupplierId);
    }
}
