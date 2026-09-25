using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// A finalidade da SC. Orçamento para em "orçamento apresentado" depois da cotação — sem aprovação —
/// e só vai ao Nível 1 quando vira compra; o Nível 1 sabe que nasceu como orçamento.
/// </summary>
public class FinalidadeDaScTests
{
    private sealed class Relogio : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class Numeros : IPrNumberGenerator
    {
        private int _n;
        public Task<string> NextAsync(CancellationToken ct = default) => Task.FromResult($"PR-2026-{++_n:000000}");
    }

    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana Solicitante", Roles.Requester);
    private static readonly Actor Bruno = new(Guid.NewGuid(), "Bruno Aprovador", Roles.Approver);
    private static readonly Actor Carla = new(Guid.NewGuid(), "Carla Compradora", Roles.PurchasingOfficer);

    private sealed record Mundo(AppDbContext Db, QuotationService Rfq, RequisitionService Prs,
        TorreDeControleService Torre, Supplier Alfa);

    private static async Task<Mundo> Montar()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(new ItensDaCotacaoNoCatalogo()).Options);
        var relogio = new Relogio();
        var sup = new SupplierService(db, relogio);
        var (alfa, _) = await sup.CreateAsync(Carla.Id, "Alfa LTDA", "Alfa", "12345678000190", null, "81 3333-1000");
        await sup.SetHomologationAsync(alfa!.Id, SupplierHomologation.Homologado);
        return new(db, new QuotationService(db, relogio),
            new RequisitionService(db, new Numeros(), new CatalogService(db, relogio), relogio),
            new TorreDeControleService(db, relogio), alfa);
    }

    private static async Task<PurchaseRequisition> ScAsync(Mundo m, string? finalidade)
    {
        var (pr, erro) = await m.Prs.CreateAsync(Ana, "Levantar preço de resma", "CC-01", "NORMAL", null,
            [new ItemInput("Resma de papel A4", 10, "UN", 25, null)],
            header: new RequisitionService.ScHeaderInput(null, null, null, null, Purpose: finalidade));
        Assert.Null(erro);
        await m.Prs.SubmitAsync(Ana, pr!.Id);
        return pr;
    }

    /// <summary>Abre, cota e escolhe o vencedor — o ponto em que orçamento e compra se separam.</summary>
    private static async Task<Quotation> AteEscolherAsync(Mundo m, PurchaseRequisition pr)
    {
        var (q, e1) = await m.Rfq.CreateFromPrAsync(Carla, pr.Id, QuotationKind.Purchase, null, null);
        Assert.Null(e1);
        await m.Rfq.InviteSuppliersAsync(Carla, q!.Id, [m.Alfa.Id]);
        var item = q.Items.Single();
        var (_, e2) = await m.Rfq.SubmitProposalAsync(q.Id, m.Alfa.Id,
            new ProposalInput(15, "28 dias", 0, null, null, [new ProposalItemInput(item.Id, 24.9m, null)]), "PORTAL", "Alfa");
        Assert.Null(e2);
        await m.Rfq.CloseForAnalysisAsync(Carla, q.Id);
        var proposta = (await m.Db.Quotations.Include(x => x.Proposals).SingleAsync(x => x.Id == q.Id)).Proposals.Single();
        var (escolhido, e3) = await m.Rfq.SelectWinnerAsync(Carla, q.Id, proposta.Id, null, "Único fornecedor com preço no prazo");
        Assert.Null(e3);
        return escolhido!;
    }

    // ------------------------------------------------------------------ a SC

    [Fact]
    public async Task A_SC_guarda_a_finalidade_e_recusa_a_invalida()
    {
        var m = await Montar();
        Assert.Equal(FinalidadeDaSc.Orcamento, (await ScAsync(m, "orcamento")).Purpose);
        var (sc, erro) = await m.Prs.CreateAsync(Ana, "x", "CC-01", null, null, [new ItemInput("Caneta", 1, "UN", null, null)],
            header: new RequisitionService.ScHeaderInput(null, null, null, null, Purpose: "TALVEZ"));
        Assert.Null(sc);
        Assert.Equal("PR-ERR-024", erro!.Code);
    }

    [Fact]
    public async Task Orcamento_e_compra_nao_se_misturam_no_mesmo_processo()
    {
        var m = await Montar();
        var orcamento = await ScAsync(m, FinalidadeDaSc.Orcamento);
        var compra = await ScAsync(m, FinalidadeDaSc.Compra);
        var itens = await m.Db.RequisitionItems.Where(i => i.RequisitionId == orcamento.Id || i.RequisitionId == compra.Id)
            .Select(i => i.Id).ToListAsync();
        var (q, erro) = await m.Rfq.CreateFromItemsAsync(Carla, itens, QuotationKind.Purchase, null, null);
        Assert.Null(q);
        Assert.Equal("RFQ-ERR-063", erro!.Code);
    }

    // ------------------------------------------------------------------ o caminho do orçamento

    [Fact]
    public async Task Orcamento_para_depois_da_escolha_sem_ir_ao_Nivel_1_e_avisa_quem_pediu()
    {
        var m = await Montar();
        var q = await AteEscolherAsync(m, await ScAsync(m, FinalidadeDaSc.Orcamento));

        Assert.True(q.IsBudget);
        Assert.Equal(QuotationStatus.BudgetPresented, q.Status);
        var avisos = await m.Db.UserNotices.ToListAsync();
        Assert.Contains(avisos, a => a.UserId == Ana.Id && a.Kind == AvisoKinds.OrcamentoApresentado);
        Assert.DoesNotContain(avisos, a => a.Kind == AvisoKinds.AprovacaoNivel1);
        Assert.Contains(await m.Db.ProcessEvents.ToListAsync(), e => e.QuotationId == q.Id && e.EventType == "ORCAMENTO_APRESENTADO");
    }

    [Fact]
    public async Task Virar_compra_leva_ao_Nivel_1_e_a_marca_de_orcamento_fica()
    {
        var m = await Montar();
        var q = await AteEscolherAsync(m, await ScAsync(m, FinalidadeDaSc.Orcamento));

        var (convertido, erro) = await m.Rfq.ConverterOrcamentoEmCompraAsync(Carla, q.Id);

        Assert.Null(erro);
        Assert.Equal(QuotationStatus.AwaitingManager, convertido!.Status);
        Assert.True(convertido.IsBudget);   // é o que o Nível 1 lê como "nasceu como orçamento"
        Assert.Equal("Carla Compradora", convertido.BudgetConvertedByLabel);
        Assert.NotNull(convertido.BudgetConvertedAt);
        Assert.Contains(await m.Db.ProcessEvents.ToListAsync(), e => e.QuotationId == q.Id && e.EventType == "ORCAMENTO_VIROU_COMPRA");
    }

    [Fact]
    public async Task So_o_orcamento_apresentado_vira_compra_e_so_quem_conduz_converte()
    {
        var m = await Montar();
        var compra = await AteEscolherAsync(m, await ScAsync(m, FinalidadeDaSc.Compra));
        Assert.Equal(QuotationStatus.AwaitingManager, compra.Status);   // compra segue como sempre
        var (_, naoEOrcamento) = await m.Rfq.ConverterOrcamentoEmCompraAsync(Carla, compra.Id);
        Assert.Equal("RFQ-ERR-064", naoEOrcamento!.Code);

        var orcamento = await AteEscolherAsync(m, await ScAsync(m, FinalidadeDaSc.Orcamento));
        var (_, semPapel) = await m.Rfq.ConverterOrcamentoEmCompraAsync(Ana, orcamento.Id);
        Assert.Equal("RFQ-ERR-900", semPapel!.Code);
    }

    // ------------------------------------------------------------------ corrigir a finalidade

    [Fact]
    public async Task O_comprador_corrige_a_finalidade_antes_de_cotar_e_depois_nao()
    {
        var m = await Montar();
        var sc = await ScAsync(m, FinalidadeDaSc.Compra);
        var (corrigida, erro) = await m.Prs.MudarFinalidadeAsync(Carla, sc.Id, FinalidadeDaSc.Orcamento);
        Assert.Null(erro);
        Assert.Equal(FinalidadeDaSc.Orcamento, corrigida!.Purpose);

        // enviada, já não é de quem pediu mexer
        var (_, doSolicitante) = await m.Prs.MudarFinalidadeAsync(Ana, sc.Id, FinalidadeDaSc.Compra);
        Assert.Equal("PR-ERR-001", doSolicitante!.Code);

        await m.Rfq.CreateFromPrAsync(Carla, sc.Id, QuotationKind.Purchase, null, null);
        var (_, emProcesso) = await m.Prs.MudarFinalidadeAsync(Carla, sc.Id, FinalidadeDaSc.Compra);
        Assert.Equal("PR-ERR-025", emProcesso!.Code);
    }

    // ------------------------------------------------------------------ quem lê a situação

    [Fact]
    public async Task A_Torre_diz_que_a_vez_e_de_quem_pediu_sem_cobrar_prazo_do_comprador()
    {
        var m = await Montar();
        await AteEscolherAsync(m, await ScAsync(m, FinalidadeDaSc.Orcamento));

        var linha = Assert.Single((await m.Torre.ConsultarAsync(new FiltroTorre())).Items);
        Assert.Equal("ORCAMENTO_APRESENTADO", linha.StatusKey);
        Assert.Equal("COTACAO", linha.Stage);
        Assert.Equal("Aguardando decisão do solicitante", linha.ActionLabel);
        Assert.False(linha.NeedsBuyer);
        Assert.Equal(FinalidadeDaSc.Orcamento, linha.Purpose);
        Assert.StartsWith("Decisão de quem pediu", linha.WaitingOn!.Who);
        Assert.False(linha.Sla!.Breached);
    }

    [Fact]
    public async Task A_linha_do_tempo_de_quem_pediu_diz_que_o_orcamento_esta_pronto()
    {
        var m = await Montar();
        var sc = await ScAsync(m, FinalidadeDaSc.Orcamento);
        await AteEscolherAsync(m, sc);

        var acompanhamento = (await AcompanhamentoDaSc.MontarMapAsync(m.Db, [await m.Db.Requisitions.Include(r => r.Items)
            .SingleAsync(r => r.Id == sc.Id)])).Values.Single().Acompanhamento;
        Assert.StartsWith("Orçamento pronto com Alfa", acompanhamento.Frase);
        Assert.Equal("Ana Solicitante", acompanhamento.ComQuem);
    }

    [Fact]
    public void O_caminho_do_processo_mostra_o_passo_do_orcamento()
    {
        var q = new Quotation { IsBudget = true, Status = QuotationStatus.BudgetPresented, CreatedByLabel = "Carla",
            SelectedAt = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero), SelectedByLabel = "Carla" };
        var etapas = CaminhoDoProcesso.De(q, ["Ana"], null, AlcadasDoCentro.Nenhuma);
        var orcamento = Assert.Single(etapas, e => e.Chave == "orcamento");
        Assert.Equal(CaminhoDoProcesso.Atual, orcamento.Situacao);
        // e o processo que não nasceu como orçamento não ganha o passo
        Assert.DoesNotContain(CaminhoDoProcesso.De(new Quotation { CreatedByLabel = "Carla" }, ["Ana"], null, AlcadasDoCentro.Nenhuma),
            e => e.Chave == "orcamento");
    }

    [Theory]
    [InlineData("EM_ANALISE", QuotationStatus.Analysis)]              // a chave que a tela manda
    [InlineData("ORCAMENTO_APRESENTADO", QuotationStatus.BudgetPresented)]
    [InlineData("Analysis", QuotationStatus.Analysis)]                // o nome do enum continua valendo
    public void O_filtro_de_situacao_entende_a_chave_da_tela(string pedido, QuotationStatus esperado)
    {
        Assert.Equal(esperado, TrinoSupply.Foundation.Api.Rotas.Api.StatusDoFiltro(pedido));
        Assert.Null(TrinoSupply.Foundation.Api.Rotas.Api.StatusDoFiltro(""));
    }
}
