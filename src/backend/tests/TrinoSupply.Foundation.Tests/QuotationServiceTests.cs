using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

public class QuotationServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeNumbers : IPrNumberGenerator
    {
        private int _next;
        public Task<string> NextAsync(CancellationToken ct = default) =>
            Task.FromResult($"PR-2026-{++_next:000000}");
    }

    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana Solicitante", Roles.Requester);
    private static readonly Actor Bruno = new(Guid.NewGuid(), "Bruno Aprovador", Roles.Approver);
    private static readonly Actor Carla = new(Guid.NewGuid(), "Carla Compradora", Roles.PurchasingOfficer);
    private static readonly Actor Gustavo = new(Guid.NewGuid(), "Gustavo Gestor", Roles.SupplyManager);
    private static readonly Actor Diana = new(Guid.NewGuid(), "Diana Diretora", Roles.Director);

    private sealed record World(
        QuotationService Rfq, RequisitionService Prs, SupplierService Sup,
        AppDbContext Db, Supplier Alfa, Supplier Beta, PurchaseRequisition Pr);

    private static async Task<World> BuildAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        var rfq = new QuotationService(db, clock);
        var prs = new RequisitionService(db, new FakeNumbers(), new CatalogService(db, clock), clock);
        var sup = new SupplierService(db, clock);

        var (alfa, _) = await sup.CreateAsync(Carla.Id, "Alfa LTDA", "Alfa", "12345678000190", null, null);
        var (beta, _) = await sup.CreateAsync(Carla.Id, "Beta LTDA", "Beta", "98765432000110", null, null);
        var (pr, _) = await prs.CreateAsync(Ana, "Ferramentas manutenção", "CC-01", "NORMAL", null,
            [new ItemInput("Martelete rebatedor MRP 900", 1, "UN", 900, null),
             new ItemInput("Pé de cabra", 1, "UN", 70, null)]);
        await prs.SubmitAsync(Ana, pr!.Id);
        await prs.ApproveAsync(Bruno, pr.Id, null);
        return new World(rfq, prs, sup, db, alfa!, beta!, pr);
    }

    private static ProposalInput ProposalFor(Quotation q, decimal p1, decimal p2) =>
        new(15, "28 dias", 0, null, null,
            q.Items.OrderBy(i => i.Sequence).Zip(new[] { p1, p2 })
                .Select(x => new ProposalItemInput(x.First.Id, x.Second, null)).ToList());

    private static async Task<Quotation> UpToAnalysisAsync(World w)
    {
        var (q, e1) = await w.Rfq.CreateFromPrAsync(Carla, w.Pr.Id, QuotationKind.Purchase, null, null);
        Assert.Null(e1);
        await w.Rfq.InviteSuppliersAsync(Carla, q!.Id, [w.Alfa.Id, w.Beta.Id]);
        var (_, pa) = await w.Rfq.SubmitProposalAsync(q.Id, w.Alfa.Id, ProposalFor(q, 857.65m, 65.08m), "PORTAL", "Alfa");
        Assert.Null(pa);
        await w.Rfq.SubmitProposalAsync(q.Id, w.Beta.Id, ProposalFor(q, 950m, 80m), "PORTAL", "Beta");
        var (closed, e2) = await w.Rfq.CloseForAnalysisAsync(Carla, q.Id);
        Assert.Null(e2);
        return closed!;
    }

    private static async Task<Quotation> UpToApprovedAsync(World w)
    {
        var q = await UpToAnalysisAsync(w);
        var winner = q.Proposals.First(p => p.SupplierId == w.Alfa.Id);
        var (sel, e1) = await w.Rfq.SelectWinnerAsync(Carla, q.Id, winner.Id, "Preço, Prazo", "Menor preço com mesmo prazo.");
        Assert.Null(e1);
        var (mgr, e2) = await w.Rfq.ManagerDecisionAsync(Gustavo, q.Id, "APROVAR", null);
        Assert.Null(e2);
        var (dir, e3) = await w.Rfq.DirectorDecisionAsync(Diana, q.Id, "APROVAR", null);
        Assert.Null(e3);
        Assert.Equal(QuotationStatus.ApprovedForIssue, dir!.Status);
        return dir;
    }

    /// <summary>Cadastra o centro com as listas de alçada (Nível 1 e Nível 2).</summary>
    private static async Task ComAlcadasAsync(World w, string code, Actor[] nivel1, Actor[] nivel2)
    {
        var cc = new CostCenter
        {
            Code = code, Name = "Centro de teste", Active = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        foreach (var (pessoas, level) in new[] { (nivel1, ApprovalLevels.Level1), (nivel2, ApprovalLevels.Level2) })
            foreach (var pessoa in pessoas)
                cc.Approvers.Add(new CostCenterApprover
                {
                    CostCenterId = cc.Id, UserId = pessoa.Id, UserName = pessoa.Label,
                    Level = level, CreatedAt = DateTimeOffset.UtcNow,
                });
        w.Db.CostCenters.Add(cc);
        await w.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Alcadas_do_centro_valem_sobre_o_vinculo_antigo_nos_dois_niveis()
    {
        var w = await BuildAsync();
        var elias = new Actor(Guid.NewGuid(), "Elias Nível 1", Roles.Approver);
        var fabio = new Actor(Guid.NewGuid(), "Fábio Nível 2", Roles.Director);
        await ComAlcadasAsync(w, "CC-01", [elias], [fabio]);

        var q = await UpToAnalysisAsync(w);
        var winner = q.Proposals.First(p => p.SupplierId == w.Alfa.Id);
        await w.Rfq.SelectWinnerAsync(Carla, q.Id, winner.Id, "Preço", "Menor preço.");

        var (_, foraDoNivel1) = await w.Rfq.ManagerDecisionAsync(Gustavo, q.Id, "APROVAR", null);
        Assert.Equal("RFQ-ERR-031", foraDoNivel1!.Code);
        Assert.Contains("Elias Nível 1", foraDoNivel1.Message);
        var (comNivel1, e1) = await w.Rfq.ManagerDecisionAsync(elias, q.Id, "APROVAR", null);
        Assert.Null(e1);
        Assert.Equal(QuotationStatus.AwaitingDirector, comNivel1!.Status);

        var (_, foraDoNivel2) = await w.Rfq.DirectorDecisionAsync(Diana, q.Id, "APROVAR", null);
        Assert.Equal("RFQ-ERR-032", foraDoNivel2!.Code);
        Assert.Contains("Fábio Nível 2", foraDoNivel2.Message);
        var (comNivel2, e2) = await w.Rfq.DirectorDecisionAsync(fabio, q.Id, "APROVAR", null);
        Assert.Null(e2);
        Assert.Equal(QuotationStatus.ApprovedForIssue, comNivel2!.Status);
    }

    [Fact]
    public async Task Cotacao_nasce_de_pr_aprovada_com_numero_unico_e_itens_copiados()
    {
        var w = await BuildAsync();

        var (q, error) = await w.Rfq.CreateFromPrAsync(Carla, w.Pr.Id, QuotationKind.Purchase, null, null);

        Assert.Null(error);
        Assert.StartsWith("RFQ-2026-", q!.Number);
        Assert.Equal(QuotationStatus.Open, q.Status);
        Assert.Equal(2, q.Items.Count);
        Assert.Equal(w.Pr.Number, q.SourcePrNumber);

        var (_, second) = await w.Rfq.CreateFromPrAsync(Carla, w.Pr.Id, QuotationKind.Purchase, null, null);
        Assert.Equal("RFQ-ERR-001", second!.Code); // uma cotação ativa por PR
    }

    [Fact]
    public async Task Fornecedor_inativo_nao_pode_ser_convidado_e_nao_convidado_nao_propoe()
    {
        var w = await BuildAsync();
        await w.Sup.UpdateAsync(w.Beta.Id, null, null, null, active: false);
        var (q, _) = await w.Rfq.CreateFromPrAsync(Carla, w.Pr.Id, QuotationKind.Purchase, null, null);

        var (_, inactive) = await w.Rfq.InviteSuppliersAsync(Carla, q!.Id, [w.Beta.Id]);
        Assert.Equal("RFQ-ERR-010", inactive!.Code);

        var (_, notInvited) = await w.Rfq.SubmitProposalAsync(q.Id, w.Alfa.Id, ProposalFor(q, 1, 1), "PORTAL", "Alfa");
        Assert.Equal("RFQ-ERR-050", notInvited!.Code);
    }

    [Fact]
    public async Task Propostas_sao_versionadas_ate_a_escolha_do_fornecedor()
    {
        var w = await BuildAsync();
        var q = await UpToAnalysisAsync(w);

        Assert.Equal(2, q.Proposals.Count);

        // em análise o comprador ainda lança o que o fornecedor respondeu (mapa de cotação,
        // revisão de telas 2026-08-26): entra como nova versão da proposta
        var (nova, erro) = await w.Rfq.SubmitProposalAsync(q.Id, w.Alfa.Id, ProposalFor(q, 1, 1), "INTERNO", "Carla");
        Assert.Null(erro);
        Assert.Equal(2, nova!.VersionNumber);

        // escolhido o vencedor (a versão mais recente), o mapa fecha
        var (escolhido, erroEscolha) = await w.Rfq.SelectWinnerAsync(Carla, q.Id, nova.Id, "Preço", "Menor preço.");
        Assert.Null(erroEscolha);
        Assert.Equal(QuotationStatus.AwaitingManager, escolhido!.Status);
        var (_, late) = await w.Rfq.SubmitProposalAsync(q.Id, w.Alfa.Id, ProposalFor(q, 1, 1), "PORTAL", "Alfa");
        Assert.Equal("RFQ-ERR-020", late!.Code);
    }

    [Fact]
    public async Task Fornecedor_pode_entrar_na_analise_e_o_desconto_abate_o_total()
    {
        var w = await BuildAsync();
        var q = await UpToAnalysisAsync(w);

        // slide 12: dava para convidar só com a cotação aberta; agora vale durante a análise
        var (comNovo, erro) = await w.Rfq.InviteSuppliersAsync(Carla, q.Id, [w.Beta.Id]);
        Assert.Null(erro);
        Assert.Contains(comNovo!.Suppliers, s => s.SupplierId == w.Beta.Id);

        var itens = q.Items.Select(i => new ProposalItemInput(i.Id, 10m, i.Quantity)).ToList();
        var bruto = q.Items.Sum(i => 10m * i.Quantity);
        var (proposta, semErro) = await w.Rfq.SubmitProposalAsync(q.Id, w.Beta.Id,
            new ProposalInput(5, "30 dias", 50m, null, "com desconto", itens, DiscountValue: 30m, Currency: "brl"),
            "INTERNO", "Carla");
        Assert.Null(semErro);
        Assert.Equal(bruto + 50m - 30m, proposta!.TotalValue);
        Assert.Equal("BRL", proposta.Currency);

        var (_, descontoAlto) = await w.Rfq.SubmitProposalAsync(q.Id, w.Beta.Id,
            new ProposalInput(5, null, null, null, null, itens, DiscountValue: bruto + 1m, Currency: null),
            "INTERNO", "Carla");
        Assert.Equal("RFQ-ERR-021", descontoAlto!.Code);
    }

    [Fact]
    public async Task Escolha_exige_justificativa_e_encaminha_ao_gerente()
    {
        var w = await BuildAsync();
        var q = await UpToAnalysisAsync(w);
        var winner = q.Proposals.First(p => p.SupplierId == w.Alfa.Id);

        var (_, semJust) = await w.Rfq.SelectWinnerAsync(Carla, q.Id, winner.Id, "Preço", "  ");
        Assert.Equal("RFQ-ERR-021", semJust!.Code);

        var (sel, ok) = await w.Rfq.SelectWinnerAsync(Carla, q.Id, winner.Id, "Preço, Prazo", "Menor preço.");
        Assert.Null(ok);
        Assert.Equal(QuotationStatus.AwaitingManager, sel!.Status);
        Assert.Equal(w.Alfa.Id, sel.WinnerSupplierId);
    }

    [Fact]
    public async Task SoD_quem_seleciona_nao_aprova_e_diretor_nao_repete_aprovador()
    {
        var w = await BuildAsync();
        var q = await UpToAnalysisAsync(w);
        var winner = q.Proposals.First(p => p.SupplierId == w.Alfa.Id);
        await w.Rfq.SelectWinnerAsync(Gustavo, q.Id, winner.Id, "Preço", "Menor preço.");

        var (_, self) = await w.Rfq.ManagerDecisionAsync(Gustavo, q.Id, "APROVAR", null);
        Assert.Equal("RFQ-ERR-030", self!.Code); // selecionou → não aprova

        var admin = new Actor(Guid.NewGuid(), "Root Admin", Roles.SystemAdministrator);
        await w.Rfq.ManagerDecisionAsync(admin, q.Id, "APROVAR", null);
        var (_, sameAsManager) = await w.Rfq.DirectorDecisionAsync(admin, q.Id, "APROVAR", null);
        Assert.Equal("RFQ-ERR-030", sameAsManager!.Code); // diretor ≠ aprovador gerencial
    }

    [Fact]
    public async Task Diretor_so_decide_depois_do_gerente_e_ajustes_devolvem_mantendo_historico()
    {
        var w = await BuildAsync();
        var q = await UpToAnalysisAsync(w);
        var winner = q.Proposals.First(p => p.SupplierId == w.Alfa.Id);
        await w.Rfq.SelectWinnerAsync(Carla, q.Id, winner.Id, "Preço", "Menor preço.");

        var (_, early) = await w.Rfq.DirectorDecisionAsync(Diana, q.Id, "APROVAR", null);
        Assert.Equal("RFQ-ERR-020", early!.Code); // sem aprovação gerencial antes

        var (_, semMotivo) = await w.Rfq.ManagerDecisionAsync(Gustavo, q.Id, "AJUSTES", " ");
        Assert.Equal("RFQ-ERR-021", semMotivo!.Code);

        var (adj, ok) = await w.Rfq.ManagerDecisionAsync(Gustavo, q.Id, "AJUSTES", "Renegociar frete.");
        Assert.Null(ok);
        Assert.Equal(QuotationStatus.Analysis, adj!.Status); // volta p/ Suprimentos
        var events = await w.Rfq.TimelineAsync(q.Id);
        Assert.Contains(events, e => e.EventType == "AJUSTES_SOLICITADOS");
        Assert.Contains(events, e => e.EventType == "FORNECEDOR_SELECIONADO"); // histórico preservado
    }

    [Fact]
    public async Task OC_nao_pode_ser_emitida_sem_as_duas_aprovacoes()
    {
        var w = await BuildAsync();
        var q = await UpToAnalysisAsync(w);
        var winner = q.Proposals.First(p => p.SupplierId == w.Alfa.Id);
        await w.Rfq.SelectWinnerAsync(Carla, q.Id, winner.Id, "Preço", "Menor preço.");

        var (_, tooEarly) = await w.Rfq.IssuePurchaseOrderAsync(Carla, q.Id, null);
        Assert.Equal("RFQ-ERR-040", tooEarly!.Code);
    }

    [Fact]
    public async Task Fluxo_completo_emite_OC_com_dados_da_proposta_vencedora_e_timeline()
    {
        var w = await BuildAsync();
        var q = await UpToApprovedAsync(w);

        var (order, error) = await w.Rfq.IssuePurchaseOrderAsync(Carla, q.Id, "Entregar no almoxarifado central.");

        Assert.Null(error);
        Assert.StartsWith("PO-2026-", order!.Number);
        Assert.Equal(922.73m, order.TotalValue);                 // 857,65 + 65,08 (modelo OC 663)
        Assert.Equal("28 dias", order.PaymentTerms);
        Assert.Equal(q.Number, order.QuotationNumber);
        Assert.Equal(w.Pr.Number, order.SourcePrNumber);

        var updated = await w.Rfq.GetAsync(q.Id);
        Assert.Equal(QuotationStatus.PoIssued, updated!.Status);
        Assert.Equal(order.Number, updated.PurchaseOrderNumber);

        var types = (await w.Rfq.TimelineAsync(q.Id)).Select(e => e.EventType).ToList();
        foreach (var expected in new[] { "COTACAO_ABERTA", "FORNECEDOR_CONVIDADO", "PROPOSTA_RECEBIDA",
                 "COTACAO_ENCERRADA", "FORNECEDOR_SELECIONADO", "GERENTE_APROVOU", "DIRETOR_APROVOU", "OC_EMITIDA" })
            Assert.Contains(expected, types);

        var (_, again) = await w.Rfq.IssuePurchaseOrderAsync(Carla, q.Id, null);
        Assert.Equal("RFQ-ERR-040", again!.Code); // nunca duas OCs do mesmo processo
    }

    [Fact]
    public async Task OC_bloqueada_para_fornecedor_que_ficou_inativo()
    {
        var w = await BuildAsync();
        var q = await UpToApprovedAsync(w);
        await w.Sup.UpdateAsync(w.Alfa.Id, null, null, null, active: false);

        var (_, error) = await w.Rfq.IssuePurchaseOrderAsync(Carla, q.Id, null);

        Assert.Equal("RFQ-ERR-040", error!.Code);
    }

    [Fact]
    public void Valor_por_extenso_do_modelo_oficial()
    {
        Assert.Equal("novecentos e vinte e dois reais e setenta e três centavos", NumberToWordsPtBr.Currency(922.73m));
        Assert.Equal("mil e um reais", NumberToWordsPtBr.Currency(1001m));
        Assert.Equal("cem reais", NumberToWordsPtBr.Currency(100m));
        Assert.Equal("um real e um centavo", NumberToWordsPtBr.Currency(1.01m));
        Assert.Equal("dois milhões e trezentos mil reais", NumberToWordsPtBr.Currency(2_300_000m));
    }
}
