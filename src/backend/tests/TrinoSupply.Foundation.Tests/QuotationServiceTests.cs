using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Inventory;
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
        // fornecedor novo nasce PROSPECT: participar da cotação é livre, vencer exige
        // homologação (SUP-ERR-030). O cenário destes testes é o do fornecedor já
        // homologado — quem cuida do caminho do prospect é o teste próprio dele.
        await sup.SetHomologationAsync(alfa!.Id, SupplierHomologation.Homologado);
        await sup.SetHomologationAsync(beta!.Id, SupplierHomologation.Homologado);
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

    // ---- fila de aprovação: filtra no banco, sem teto (PO-BR-012) -----------
    [Fact]
    public async Task Fila_de_aprovacao_nao_trunca_e_respeita_a_segregacao()
    {
        var w = await BuildAsync();

        // uma fila maior que o antigo teto de 200 da listagem
        for (var i = 0; i < 210; i++)
            w.Db.Quotations.Add(new Quotation
            {
                Number = $"RFQ-2026-{i:000000}", Kind = QuotationKind.Purchase,
                Status = QuotationStatus.AwaitingManager, CostCenter = "BAH-001",
                SelectedBy = Carla.Id, CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i),
            });
        await w.Db.SaveChangesAsync();

        var fila = await w.Rfq.PendingApprovalsAsync(Roles.SupplyManager, Gustavo.Id);

        // antes, a listagem cortava em 200 e os mais antigos sumiam da fila
        Assert.Equal(210, fila.Count);
        // e a fila abre pelo mais antigo, que é o que espera há mais tempo
        Assert.Equal("RFQ-2026-000000", fila[0].Number);

        // quem selecionou o fornecedor não vê nenhum deles (RFQ-ERR-030)
        Assert.Empty(await w.Rfq.PendingApprovalsAsync(Roles.SupplyManager, Carla.Id));
        // quem não tem alçada nenhuma também não
        Assert.Empty(await w.Rfq.PendingApprovalsAsync(Roles.Requester, Gustavo.Id));
    }

    /// <summary>
    /// A listagem trazia no máximo 200 processos e não dizia que havia mais:
    /// quem procurava um processo antigo recebia "nada encontrado" para
    /// cotação que existe (PO-BR-012).
    /// </summary>
    [Fact]
    public async Task Lista_diz_quantos_existem_e_filtra_a_situacao_no_banco()
    {
        var w = await BuildAsync();
        for (var i = 0; i < 210; i++)
            w.Db.Quotations.Add(new Quotation
            {
                Number = $"RFQ-2026-{i:000000}", Kind = QuotationKind.Purchase,
                Status = i < 30 ? QuotationStatus.Cancelled : QuotationStatus.Open,
                CostCenter = "BAH-001",
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i),
            });
        await w.Db.SaveChangesAsync();

        // antes o teto era 200 e o resto sumia sem aviso
        var (pagina, total) = await w.Rfq.ListAsync(tamanho: 100);
        Assert.Equal(100, pagina.Count);
        Assert.Equal(210, total);

        var (tudo, _) = await w.Rfq.ListAsync(tamanho: 500);
        Assert.Equal(210, tudo.Count);

        // a situação é filtrada no banco, e o total acompanha o filtro
        var (canceladas, totalCancelada) = await w.Rfq.ListAsync(situacao: QuotationStatus.Cancelled);
        Assert.Equal(30, totalCancelada);
        Assert.All(canceladas, q => Assert.Equal(QuotationStatus.Cancelled, q.Status));
    }

    [Fact]
    public async Task Fila_do_nivel_2_exclui_quem_deu_o_nivel_1()
    {
        var w = await BuildAsync();
        w.Db.Quotations.Add(new Quotation
        {
            Number = "RFQ-2026-000900", Kind = QuotationKind.Purchase,
            Status = QuotationStatus.AwaitingDirector, CostCenter = "BAH-001",
            SelectedBy = Carla.Id, ManagerApprovedBy = Gustavo.Id,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });
        await w.Db.SaveChangesAsync();

        Assert.Single(await w.Rfq.PendingApprovalsAsync(Roles.Director, Diana.Id));
        Assert.Empty(await w.Rfq.PendingApprovalsAsync(Roles.Director, Gustavo.Id));  // deu o Nível 1
        Assert.Empty(await w.Rfq.PendingApprovalsAsync(Roles.Director, Carla.Id));    // selecionou
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

    /// <summary>
    /// Contrato de parceria: o fornecedor tem produtos com preço, prazo de pagamento e prazo
    /// de entrega fixos, e o contrato só vale dentro da vigência (revisão de cadastro 2026-08-27).
    /// </summary>
    [Fact]
    public async Task Contrato_de_parceria_guarda_preco_e_prazos_e_respeita_a_vigencia()
    {
        var w = await BuildAsync();
        var hoje = new DateOnly(2026, 8, 27);

        var (semItens, erroVigencia) = await w.Sup.SaveContractAsync(
            w.Alfa.Id, "CT-2026-014", hoje, hoje.AddDays(-1), null, []);
        Assert.Null(semItens);
        Assert.Equal("SUP-ERR-020", erroVigencia!.Code);

        var (_, erroPreco) = await w.Sup.SaveContractAsync(w.Alfa.Id, "CT-2026-014", hoje, hoje.AddMonths(6), null,
            [new SupplierService.ContractItemInput(null, "Martelete rebatedor MRP 900", "MRP-900", "UN", 0m, null, null, null, null)]);
        Assert.Equal("SUP-ERR-022", erroPreco!.Code);

        var (comContrato, error) = await w.Sup.SaveContractAsync(w.Alfa.Id, "CT-2026-014", hoje, hoje.AddMonths(6), "reajuste anual",
            [new SupplierService.ContractItemInput(null, "Martelete rebatedor MRP 900", "MRP-900", "UN", 820m, "30/60 dias", 28, 10, null)]);
        Assert.Null(error);
        var linha = Assert.Single(comContrato!.ContractItems);
        Assert.Equal(820m, linha.UnitPrice);
        Assert.Equal(28, linha.PaymentDays);
        Assert.Equal(10, linha.DeliveryDays);
        Assert.True(comContrato.ContractIsCurrent(hoje));
        Assert.False(comContrato.ContractIsCurrent(hoje.AddYears(1)));   // fora da vigência

        // lista vazia encerra o contrato: o fornecedor volta a ser cotado normalmente
        var (encerrado, semErro) = await w.Sup.SaveContractAsync(w.Alfa.Id, null, null, null, null, []);
        Assert.Null(semErro);
        Assert.Empty(encerrado!.ContractItems);
        Assert.False(encerrado.ContractIsCurrent(hoje));
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
        await w.Sup.UpdateAsync(w.Beta.Id, null, null, null, false);
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
    public async Task Pre_cadastro_cota_mas_so_vence_depois_de_homologado()
    {
        // O comprador cota com muita gente e só cadastra de verdade quem ganha o BID.
        // O sistema atende isso pelo PROSPECT: entra na cotação com razão social e CNPJ,
        // concorre em pé de igualdade, e a homologação é cobrada na hora de vencer — não
        // na hora de convidar.
        //
        // Antes disso, o fornecedor novo nascia HOMOLOGADO por padrão: um cadastro de dois
        // campos já saía apto a ganhar o processo sem uma certidão sequer, e o SUP-ERR-030
        // nunca disparava para quem tinha acabado de entrar.
        var w = await BuildAsync();

        var (gama, erroCadastro) = await w.Sup.CreateAsync(
            Carla.Id, "Gama Distribuidora LTDA", null, "11222333000181", null, null);
        Assert.Null(erroCadastro);
        Assert.Equal(SupplierHomologation.Prospect, gama!.HomologationStatus);

        var (q, _) = await w.Rfq.CreateFromPrAsync(Carla, w.Pr.Id, QuotationKind.Purchase, null, null);
        // convidar o prospect é livre: é isso que permite cotar antes de cadastrar de verdade
        var (comConvite, erroConvite) = await w.Rfq.InviteSuppliersAsync(Carla, q!.Id, [w.Alfa.Id, gama.Id]);
        Assert.Null(erroConvite);
        Assert.Contains(comConvite!.Suppliers, f => f.SupplierId == gama.Id);

        await w.Rfq.SubmitProposalAsync(q.Id, w.Alfa.Id, ProposalFor(q, 900, 70), "INTERNO", "Alfa");
        await w.Rfq.SubmitProposalAsync(q.Id, gama.Id, ProposalFor(q, 800, 60), "INTERNO", "Gama");
        var (emAnalise, erroFechar) = await w.Rfq.CloseForAnalysisAsync(Carla, q.Id);
        Assert.Null(erroFechar);
        var daGama = emAnalise!.Proposals.First(p => p.SupplierId == gama.Id);

        // ganhou no preço, mas não passa sem homologação
        var (_, barrado) = await w.Rfq.SelectWinnerAsync(
            Carla, q.Id, daGama.Id, "Preço", "Menor preço do processo.");
        Assert.Equal("SUP-ERR-030", barrado!.Code);
        Assert.Contains("PROSPECT", barrado.Message);

        // homologado, o mesmo vencedor passa — nada mais muda no processo
        await w.Sup.SetHomologationAsync(gama.Id, SupplierHomologation.Homologado);
        var (escolhido, ok) = await w.Rfq.SelectWinnerAsync(
            Carla, q.Id, daGama.Id, "Preço", "Menor preço do processo.");
        Assert.Null(ok);
        Assert.Equal(gama.Id, escolhido!.WinnerSupplierId);
        Assert.Equal(QuotationStatus.AwaitingManager, escolhido.Status);
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
    public async Task OC_nao_pode_ser_registrada_sem_as_duas_aprovacoes()
    {
        var w = await BuildAsync();
        var q = await UpToAnalysisAsync(w);
        var winner = q.Proposals.First(p => p.SupplierId == w.Alfa.Id);
        await w.Rfq.SelectWinnerAsync(Carla, q.Id, winner.Id, "Preço", "Menor preço.");

        var (_, tooEarly) = await w.Rfq.RegisterErpPurchaseOrderAsync(Carla, q.Id, "663", null, null);
        Assert.Equal("RFQ-ERR-040", tooEarly!.Code);
    }

    /// <summary>
    /// A O.C. do SENIOR é única no sistema, e os dois caminhos de registro precisam
    /// concordar sobre isso.
    ///
    /// A conferência do processo de cotação olhava só `Number`. Nesse caminho o pedido
    /// nasce com `Number == ErpNumber`, então ela pegava a repetição vinda dele mesmo —
    /// mas não a que vem da tela do pedido, onde o pedido guarda a própria numeração
    /// `PO-ano-sequência` e o número do SENIOR fica só em `ErpNumber`. Pelo outro lado a
    /// trava existia. O furo era de mão única, e é exatamente o que o índice único no
    /// banco pegaria.
    /// </summary>
    [Fact]
    public async Task OC_registrada_pela_tela_do_pedido_nao_pode_voltar_pela_cotacao()
    {
        var w = await BuildAsync();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        var pos = new PurchaseOrderService(w.Db, new InventoryService(w.Db, clock), clock);

        // uma compra avulsa fecha com a O.C. OC-9001: o pedido mantém PO-…, e OC-9001
        // fica só no ErpNumber
        var (avulso, _) = await pos.CreateAsync(Carla, w.Alfa.Id, null,
            [new PoItemInput("Martelete", 1, "UN", 900m, null)], null);
        var (comOc, semErro) = await pos.RegisterErpOrderAsync(Carla, avulso!.Id, "OC-9001", null);
        Assert.Null(semErro);
        Assert.Equal("OC-9001", comOc!.ErpNumber);
        Assert.NotEqual("OC-9001", comOc.Number);

        // o mesmo número, agora pelo processo de cotação, tem de ser recusado
        var q = await UpToApprovedAsync(w);
        var (nada, repetida) = await w.Rfq.RegisterErpPurchaseOrderAsync(Carla, q.Id, "OC-9001", null, null);
        Assert.Null(nada);
        Assert.Equal("RFQ-ERR-041", repetida!.Code);
        Assert.Contains("já está registrada", repetida.Message);
    }

    [Fact]
    public async Task Processo_sem_oc_do_erp_so_fecha_com_a_observacao()
    {
        var w = await BuildAsync();
        var q = await UpToApprovedAsync(w);

        var (pedido, erro) = await w.Rfq.RegisterErpPurchaseOrderAsync(Carla, q.Id, null, new DateOnly(2026, 9, 1), null,
            noErpReason: "Compra emergencial fechada direto com o fornecedor, sem O.C. no SENIOR.");

        Assert.Null(erro);
        Assert.Null(pedido!.ErpNumber);            // nenhuma O.C. inventada
        Assert.StartsWith("PO-", pedido.Number);   // o pedido usa a própria numeração
        Assert.Contains("emergencial", pedido.NoErpReason);
    }

    [Fact]
    public async Task Fluxo_completo_registra_a_OC_do_SENIOR_com_os_dados_da_proposta_e_timeline()
    {
        var w = await BuildAsync();
        var q = await UpToApprovedAsync(w);

        // campo em branco não é mais recusa seca: passa a cobrar o motivo (PO-BR-011)
        var (semNumero, faltaNumero) = await w.Rfq.RegisterErpPurchaseOrderAsync(Carla, q.Id, "  ", null, null);
        Assert.Null(semNumero);
        Assert.Equal("RFQ-ERR-043", faltaNumero!.Code);

        var (motivoCurto, curto) = await w.Rfq.RegisterErpPurchaseOrderAsync(Carla, q.Id, null, null, null,
            noErpReason: "urgente");
        Assert.Null(motivoCurto);
        Assert.Equal("RFQ-ERR-043", curto!.Code);

        var (order, error) = await w.Rfq.RegisterErpPurchaseOrderAsync(
            Carla, q.Id, "663", new DateOnly(2026, 8, 26), "Entregar no almoxarifado central.");

        Assert.Null(error);
        Assert.Equal("663", order!.Number);                      // o número é o da OC fechada no SENIOR
        Assert.Equal("663", order.ErpNumber);
        Assert.Equal(new DateOnly(2026, 8, 26), order.ErpIssuedOn);
        Assert.Equal(922.73m, order.TotalValue);                 // 857,65 + 65,08 (modelo OC 663)
        Assert.Equal("28 dias", order.PaymentTerms);
        Assert.Equal(q.Number, order.QuotationNumber);
        Assert.Equal(w.Pr.Number, order.SourcePrNumber);

        var updated = await w.Rfq.GetAsync(q.Id);
        Assert.Equal(QuotationStatus.PoIssued, updated!.Status);
        Assert.Equal(order.Number, updated.PurchaseOrderNumber);

        var types = (await w.Rfq.TimelineAsync(q.Id)).Select(e => e.EventType).ToList();
        foreach (var expected in new[] { "COTACAO_ABERTA", "FORNECEDOR_CONVIDADO", "PROPOSTA_RECEBIDA",
                 "COTACAO_ENCERRADA", "FORNECEDOR_SELECIONADO", "GERENTE_APROVOU", "DIRETOR_APROVOU", "OC_REGISTRADA" })
            Assert.Contains(expected, types);

        var (_, again) = await w.Rfq.RegisterErpPurchaseOrderAsync(Carla, q.Id, "664", null, null);
        Assert.Equal("RFQ-ERR-040", again!.Code); // nunca duas OCs do mesmo processo
    }

    /// <summary>OTIF (V2-P1): a data prometida congela no registro da O.C. e o OTIF deriva da entrega.</summary>
    [Fact]
    public async Task Registro_da_OC_congela_a_data_prometida_para_o_OTIF()
    {
        var w = await BuildAsync();
        var q = await UpToApprovedAsync(w);   // proposta vencedora com prazo de 28 dias? -> ProposalFor usa DeliveryDays 15

        var (order, error) = await w.Rfq.RegisterErpPurchaseOrderAsync(
            Carla, q.Id, "800", new DateOnly(2026, 8, 27), null);
        Assert.Null(error);

        var prazo = q.Proposals.Single(p => p.Id == q.WinnerProposalId).DeliveryDays!.Value;
        Assert.Equal(new DateOnly(2026, 8, 27).AddDays(prazo), order!.PromisedDate);
        Assert.Null(order.Otif);   // sem entrega encerrada, não há OTIF

        // entrega completa dentro do prazo: OTIF OK
        foreach (var item in order.Items) item.ReceivedQuantity = item.Quantity;
        order.DeliveryCompletedAt = new DateTimeOffset(new DateOnly(2026, 8, 30).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        Assert.True(order.OnTime);
        Assert.True(order.InFull);
        Assert.True(order.Otif);

        // entrega depois da data prometida: falha o On-Time
        order.DeliveryCompletedAt = new DateTimeOffset(
            order.PromisedDate!.Value.AddDays(3).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        Assert.False(order.OnTime);
        Assert.False(order.Otif);

        // saldo encerrado sem chegar tudo: falha o In-Full
        order.Items.First().ReceivedQuantity -= 1;
        Assert.False(order.InFull);
    }

    [Fact]
    public async Task Numero_de_OC_ja_registrado_em_outro_processo_e_recusado()
    {
        var w = await BuildAsync();
        var q = await UpToApprovedAsync(w);
        // a OC 663 já veio do SENIOR para outra compra
        w.Db.PurchaseOrders.Add(new PurchaseOrder
        {
            Number = "663", ErpNumber = "663", SupplierId = w.Beta.Id, SupplierName = "Beta",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await w.Db.SaveChangesAsync();

        var (duplicado, duplicada) = await w.Rfq.RegisterErpPurchaseOrderAsync(Carla, q.Id, "663", null, null);
        Assert.Null(duplicado);
        Assert.Equal("RFQ-ERR-041", duplicada!.Code);
    }

    /// <summary>V2-P2: total da proposta soma impostos e outros custos.</summary>
    [Fact]
    public async Task Total_da_proposta_soma_impostos_e_outros_custos()
    {
        var w = await BuildAsync();
        var (q, _) = await w.Rfq.CreateFromPrAsync(Carla, w.Pr.Id, QuotationKind.Purchase, null, null);
        await w.Rfq.InviteSuppliersAsync(Carla, q!.Id, [w.Alfa.Id]);
        var input = new ProposalInput(10, "30 dias", 50m, null, null,
            q.Items.OrderBy(i => i.Sequence).Zip(new[] { 100m, 10m })
                .Select(x => new ProposalItemInput(x.First.Id, x.Second, null)).ToList(),
            DiscountValue: 20m, TaxValue: 15m, OtherCosts: 5m);
        var (proposal, error) = await w.Rfq.SubmitProposalAsync(q.Id, w.Alfa.Id, input, "INTERNO", "Carla");
        Assert.Null(error);
        // itens 100 + 10 = 110; + frete 50 + impostos 15 + outros 5 − desconto 20 = 160
        Assert.Equal(160m, proposal!.TotalValue);
        Assert.Equal(15m, proposal.TaxValue);
        Assert.Equal(5m, proposal.OtherCosts);
    }

    /// <summary>V2-P2: saving de referência congelado no registro da O.C. (× último preço pago).</summary>
    [Fact]
    public async Task Registro_da_OC_congela_o_saving_de_referencia_contra_o_ultimo_preco_pago()
    {
        var w = await BuildAsync();
        // item de catálogo para ter histórico de preço
        var catalog = new CatalogService(w.Db, new FixedTimeProvider(DateTimeOffset.UtcNow));
        var (item, _) = await catalog.CreateAsync(Carla.Id, "REF-001", "Tinta epóxi 20L", "MANUTENCAO", "LT", 400m);

        // pedido antigo pago a 400 (histórico)
        var antigo = new PurchaseOrder
        {
            Number = "OLD-1", SupplierId = w.Beta.Id, SupplierName = "Beta",
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-2), UpdatedAt = DateTimeOffset.UtcNow.AddMonths(-2),
        };
        antigo.Items.Add(new PurchaseOrderItem
        {
            OrderId = antigo.Id, Description = "Tinta epóxi 20L", Quantity = 10, UnitPrice = 400m,
            CatalogItemId = item!.Id, CatalogCode = item.Code, CreatedAt = antigo.CreatedAt,
        });
        w.Db.PurchaseOrders.Add(antigo);
        await w.Db.SaveChangesAsync();

        // processo novo comprando o mesmo item a 380
        var (pr, _) = await new RequisitionService(w.Db, new FakeNumbers(), catalog, new FixedTimeProvider(DateTimeOffset.UtcNow))
            .CreateAsync(Ana, "Pintura", "CC-01", "NORMAL", null,
                [new ItemInput("Tinta epóxi 20L", 20, "LT", 400m, null, item.Id)]);
        await w.Prs.SubmitAsync(Ana, pr!.Id);
        await w.Prs.ApproveAsync(Bruno, pr.Id, null);
        var (q, _) = await w.Rfq.CreateFromPrAsync(Carla, pr.Id, QuotationKind.Purchase, null, null);
        await w.Rfq.InviteSuppliersAsync(Carla, q!.Id, [w.Alfa.Id]);
        await w.Rfq.SubmitProposalAsync(q.Id, w.Alfa.Id,
            new ProposalInput(10, null, 0, null, null,
                [new ProposalItemInput(q.Items.Single().Id, 380m, null)]), "INTERNO", "Carla");
        await w.Rfq.CloseForAnalysisAsync(Carla, q.Id);
        q = (await w.Rfq.GetAsync(q.Id))!;
        await w.Rfq.SelectWinnerAsync(Carla, q.Id, q.Proposals.Single().Id, "Preço", "Único.");
        await w.Rfq.ManagerDecisionAsync(Gustavo, q.Id, "APROVAR", null);
        await w.Rfq.DirectorDecisionAsync(Diana, q.Id, "APROVAR", null);

        var (order, error) = await w.Rfq.RegisterErpPurchaseOrderAsync(Carla, q.Id, "REF-OC", null, null);
        Assert.Null(error);
        var linha = order!.Items.Single();
        Assert.Equal(400m, linha.LastPaidUnitPrice);
        Assert.Equal((400m - 380m) * 20, linha.ReferenceSaving);   // 400 congelado no registro
    }

    /// <summary>V2-P2: O.C. acima do teto do contrato exige justificativa (CT-ERR-010).</summary>
    [Fact]
    public async Task OC_acima_do_teto_do_contrato_exige_justificativa()
    {
        var w = await BuildAsync();
        // o relógio do serviço é fixo em 2026-08-24: a vigência precisa cobrir essa data
        var hoje = new DateOnly(2026, 8, 24);
        await w.Sup.SaveContractAsync(w.Alfa.Id, "CT-100", hoje.AddDays(-1), hoje.AddMonths(6), null,
            [new SupplierService.ContractItemInput(null, "Martelete rebatedor MRP 900", null, "UN", 850m, null, null, null, null)],
            valueLimit: 500m);   // teto proposital abaixo do total do pedido (922,73)

        var q = await UpToApprovedAsync(w);
        var (bloqueado, teto) = await w.Rfq.RegisterErpPurchaseOrderAsync(Carla, q.Id, "CT-OC-1", null, null);
        Assert.Null(bloqueado);
        Assert.Equal("CT-ERR-010", teto!.Code);

        var (order, ok) = await w.Rfq.RegisterErpPurchaseOrderAsync(Carla, q.Id, "CT-OC-1", null, null,
            "compra emergencial autorizada pela diretoria");
        Assert.Null(ok);
        Assert.NotNull(order);
        Assert.Contains("CONTRATO_TETO_EXCEDIDO", (await w.Rfq.TimelineAsync(q.Id)).Select(e => e.EventType));
    }

    [Fact]
    public async Task Negociacao_registra_o_ganho_sobre_a_primeira_proposta()
    {
        var w = await BuildAsync();
        var q = await UpToAnalysisAsync(w);
        var alfa = q.Proposals.First(p => p.SupplierId == w.Alfa.Id);
        var original = alfa.TotalValue;                            // 922,73

        var (acima, erroAcima) = await w.Rfq.RegisterNegotiationAsync(
            Carla, q.Id, w.Alfa.Id, original + 10, null, null);
        Assert.Null(acima);
        Assert.Equal("RFQ-ERR-022", erroAcima!.Code);              // valor maior não é ganho

        var (nova, error) = await w.Rfq.RegisterNegotiationAsync(
            Carla, q.Id, w.Alfa.Id, null, 5m, "5% após negociação de prazo");
        Assert.Null(error);
        Assert.Equal(2, nova!.VersionNumber);
        Assert.Equal(Math.Round(original * 0.95m, 2), nova.TotalValue);

        var comGanho = await w.Rfq.GetAsync(q.Id);
        Assert.Equal(original, comGanho!.BaselineValue);
        Assert.Equal(nova.TotalValue, comGanho.NegotiatedValue);
        Assert.Equal(original - nova.TotalValue, comGanho.SavingValue);
        Assert.Equal(5m, comGanho.SavingPercent);
        Assert.Contains("NEGOCIACAO_REGISTRADA", (await w.Rfq.TimelineAsync(q.Id)).Select(e => e.EventType));

        // a escolha do vencedor mantém o ganho apurado contra a primeira proposta dele
        var atual = comGanho.Proposals.Where(p => p.SupplierId == w.Alfa.Id).OrderByDescending(p => p.VersionNumber).First();
        var (escolhida, erroEscolha) = await w.Rfq.SelectWinnerAsync(Carla, q.Id, atual.Id, "Preço", "Melhor proposta após negociação.");
        Assert.Null(erroEscolha);
        Assert.Equal(original - nova.TotalValue, escolhida!.SavingValue);
    }

    /// <summary>V2-P2: prospect participa da cotação, mas a seleção exige homologado (SUP-ERR-030).</summary>
    [Fact]
    public async Task Fornecedor_nao_homologado_participa_mas_nao_e_selecionado()
    {
        var w = await BuildAsync();
        // Alfa vira prospect (novo na praça); Beta segue homologado (grandfathering)
        await w.Sup.SetHomologationAsync(w.Alfa.Id, "PROSPECT");

        var q = await UpToAnalysisAsync(w);   // Alfa foi convidado e propôs normalmente
        var deAlfa = q.Proposals.First(p => p.SupplierId == w.Alfa.Id);
        var (naoSelecionado, bloqueio) = await w.Rfq.SelectWinnerAsync(Carla, q.Id, deAlfa.Id, "Preço", "Menor preço.");
        Assert.Null(naoSelecionado);
        Assert.Equal("SUP-ERR-030", bloqueio!.Code);
        Assert.Contains("PROSPECT", bloqueio.Message);

        // homologou: a seleção passa
        await w.Sup.SetHomologationAsync(w.Alfa.Id, "HOMOLOGADO");
        var (ok, nenhum) = await w.Rfq.SelectWinnerAsync(Carla, q.Id, deAlfa.Id, "Preço", "Menor preço.");
        Assert.Null(nenhum);
        Assert.Equal(QuotationStatus.AwaitingManager, ok!.Status);
    }

    /// <summary>V2-P2: certidão vencida restringe automaticamente quem tem certidões cadastradas.</summary>
    [Fact]
    public async Task Certidao_vencida_restringe_o_fornecedor_na_selecao()
    {
        var w = await BuildAsync();
        var hoje = new DateOnly(2026, 8, 24);   // relógio fixo do serviço
        await w.Sup.AddDocumentAsync(w.Alfa.Id, "CND_FEDERAL", null, hoje.AddDays(-1),
            Guid.NewGuid(), "cnd.pdf", "Carla");

        var q = await UpToAnalysisAsync(w);
        var deAlfa = q.Proposals.First(p => p.SupplierId == w.Alfa.Id);
        var (_, restrito) = await w.Rfq.SelectWinnerAsync(Carla, q.Id, deAlfa.Id, "Preço", "Menor preço.");
        Assert.Equal("SUP-ERR-030", restrito!.Code);
        Assert.Contains("RESTRITO", restrito.Message);

        // fornecedor sem NENHUMA certidão cadastrada não é punido (Beta segue selecionável)
        var deBeta = q.Proposals.First(p => p.SupplierId == w.Beta.Id);
        var (okBeta, nenhum) = await w.Rfq.SelectWinnerAsync(Carla, q.Id, deBeta.Id, "Preço", "Beta sem certidões cadastradas.");
        Assert.Null(nenhum);
        Assert.NotNull(okBeta);

        // bloqueado não é nem convidado
        await w.Sup.SetHomologationAsync(w.Beta.Id, "BLOQUEADO");
        var (pr2, _) = await w.Prs.CreateAsync(Ana, "Outra compra", "CC-01", "NORMAL", null,
            [new ItemInput("Chave de fenda", 1, "UN", 20, null)]);
        await w.Prs.SubmitAsync(Ana, pr2!.Id);
        await w.Prs.ApproveAsync(Bruno, pr2.Id, null);
        var (q2, _) = await w.Rfq.CreateFromPrAsync(Carla, pr2.Id, QuotationKind.Purchase, null, null);
        var (_, convite) = await w.Rfq.InviteSuppliersAsync(Carla, q2!.Id, [w.Beta.Id]);
        Assert.Equal("RFQ-ERR-010", convite!.Code);
    }

    [Fact]
    public async Task OC_bloqueada_para_fornecedor_que_ficou_inativo()
    {
        var w = await BuildAsync();
        var q = await UpToApprovedAsync(w);
        await w.Sup.UpdateAsync(w.Alfa.Id, null, null, null, active: false);

        var (_, error) = await w.Rfq.RegisterErpPurchaseOrderAsync(Carla, q.Id, "663", null, null);

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

    // ==== pleito de reajuste do contrato (V2-P4 — cost avoidance) ===============

    [Fact]
    public async Task Pleito_de_reajuste_congela_o_custo_evitado_sobre_o_consumo_de_12_meses()
    {
        var w = await BuildAsync();
        var hoje = DateOnly.FromDateTime(new DateTime(2026, 8, 24));
        await w.Sup.SaveContractAsync(w.Alfa.Id, "CT-CA", hoje.AddDays(-30), hoje.AddDays(300), null,
            [new SupplierService.ContractItemInput(null, "Martelete tabelado", null, "UN", 100m, null, null, null, null)]);
        var q = await UpToApprovedAsync(w);
        var (order, _) = await w.Rfq.RegisterErpPurchaseOrderAsync(Carla, q.Id, "OC-CA", null, null);
        Assert.NotNull(order);   // consumo 12m = total da O.C.

        // pediu 10%, fechou 4%: evitado = 6% da base; preços do contrato reajustados em 4%
        var (adj, error) = await w.Sup.RegisterContractAdjustmentAsync(
            Carla, w.Alfa.Id, 10m, 4m, "Reajuste anual", applyToPrices: true);
        Assert.Null(error);
        Assert.Equal(order!.TotalValue, adj!.BaseValue);
        Assert.Equal(Math.Round(0.06m * order.TotalValue, 2), adj.CostAvoidance);
        var item = await w.Db.Set<SupplierContractItem>().SingleAsync(i => i.SupplierId == w.Alfa.Id);
        Assert.Equal(104m, item.UnitPrice);

        // aceito acima do pleiteado não existe (CT-ERR-021); sem contrato não registra (CT-ERR-022)
        var (_, acima) = await w.Sup.RegisterContractAdjustmentAsync(Carla, w.Alfa.Id, 5m, 8m, null, false);
        Assert.Equal("CT-ERR-021", acima!.Code);
        var (_, semContrato) = await w.Sup.RegisterContractAdjustmentAsync(Carla, w.Beta.Id, 10m, 0m, null, false);
        Assert.Equal("CT-ERR-022", semContrato!.Code);
    }

    // ==== agrupamento multi-SC (V2 — regra 1 generalizada) ======================

    private static async Task<PurchaseRequisition> SegundaScAprovadaAsync(World w, string cc = "CC-01")
    {
        var (pr2, e) = await w.Prs.CreateAsync(Ana, "Consumíveis da oficina", cc, "NORMAL", null,
            [new ItemInput("Luva nitrílica", 10, "PAR", 8, null)]);
        Assert.Null(e);
        await w.Prs.SubmitAsync(Ana, pr2!.Id);
        await w.Prs.ApproveAsync(Bruno, pr2.Id, null);
        return pr2;
    }

    private static ProposalInput ProposalForAll(Quotation q, params decimal[] precos) =>
        new(15, "28 dias", 0, null, null,
            q.Items.OrderBy(i => i.Sequence).Zip(precos)
                .Select(x => new ProposalItemInput(x.First.Id, x.Second, null)).ToList());

    [Fact]
    public async Task Agrupamento_junta_itens_de_varias_SCs_do_mesmo_centro_e_aprova_todas_no_fim()
    {
        var w = await BuildAsync();
        var pr2 = await SegundaScAprovadaAsync(w);
        var itens = w.Pr.Items.Select(i => i.Id).Concat(pr2.Items.Select(i => i.Id)).ToList();

        var (q, error) = await w.Rfq.CreateFromItemsAsync(Carla, itens, QuotationKind.Purchase, null, null);

        Assert.Null(error);
        Assert.Equal(3, q!.Items.Count);
        Assert.Equal(2, q.SourcePrNumbers.Count);
        Assert.All(q.Items, i => Assert.NotNull(i.SourcePrNumber));
        Assert.Contains(q.Items, i => i.SourcePrId == pr2.Id);

        // as duas SCs saem da fila de "aguardando cotação"
        var (ready, _) = await w.Rfq.QueueAsync();
        Assert.DoesNotContain(ready, e => e.Pr.Id == w.Pr.Id || e.Pr.Id == pr2.Id);

        // fluxo completo: proposta → seleção → alçadas → O.C. registrada
        await w.Rfq.InviteSuppliersAsync(Carla, q.Id, [w.Alfa.Id]);
        var (prop, pe) = await w.Rfq.SubmitProposalAsync(q.Id, w.Alfa.Id, ProposalForAll(q, 800m, 60m, 7m), "PORTAL", "Alfa");
        Assert.Null(pe);
        await w.Rfq.CloseForAnalysisAsync(Carla, q.Id);
        var (_, se) = await w.Rfq.SelectWinnerAsync(Carla, q.Id, prop!.Id, "Preço", "Única proposta com preço adequado.");
        Assert.Null(se);
        await w.Rfq.ManagerDecisionAsync(Gustavo, q.Id, "APROVAR", null);
        var (dir, de) = await w.Rfq.DirectorDecisionAsync(Diana, q.Id, "APROVAR", null);
        Assert.Null(de);

        // a autorização da diretoria aprova TODAS as SCs de origem, não só a primária
        var sc1 = await w.Db.Requisitions.SingleAsync(r => r.Id == w.Pr.Id);
        var sc2 = await w.Db.Requisitions.SingleAsync(r => r.Id == pr2.Id);
        Assert.Equal(RequisitionStatus.Approved, sc1.Status);
        Assert.Equal(RequisitionStatus.Approved, sc2.Status);

        // a O.C. registrada guarda a SC de origem em cada item (rateio visível)
        var (order, oe) = await w.Rfq.RegisterErpPurchaseOrderAsync(Carla, dir!.Id, "OC-777", null, null);
        Assert.Null(oe);
        Assert.Equal(2, order!.Items.Select(i => i.SourcePrNumber).Distinct().Count());
        Assert.Contains(order.Items, i => i.SourcePrNumber == pr2.Number);
    }

    [Fact]
    public async Task Agrupamento_recusa_SCs_de_centros_de_custo_diferentes()
    {
        var w = await BuildAsync();
        var pr2 = await SegundaScAprovadaAsync(w, cc: "CC-02");
        var itens = w.Pr.Items.Select(i => i.Id).Concat(pr2.Items.Select(i => i.Id)).ToList();

        var (q, error) = await w.Rfq.CreateFromItemsAsync(Carla, itens, QuotationKind.Purchase, null, null);

        Assert.Null(q);
        Assert.Equal("RFQ-ERR-061", error!.Code);
    }

    [Fact]
    public async Task Item_que_ja_esta_em_processo_ativo_nao_entra_em_novo_agrupamento()
    {
        var w = await BuildAsync();
        var pr2 = await SegundaScAprovadaAsync(w);
        var (q1, e1) = await w.Rfq.CreateFromPrAsync(Carla, w.Pr.Id, QuotationKind.Purchase, null, null);
        Assert.Null(e1);

        var itens = w.Pr.Items.Select(i => i.Id).Concat(pr2.Items.Select(i => i.Id)).ToList();
        var (q2, error) = await w.Rfq.CreateFromItemsAsync(Carla, itens, QuotationKind.Purchase, null, null);

        Assert.Null(q2);
        Assert.Equal("RFQ-ERR-062", error!.Code);
        Assert.Contains(q1!.Number, error.Message);
    }

    [Fact]
    public async Task Fluxo_1para1_continua_valendo_e_agora_rastreia_a_origem_por_item()
    {
        var w = await BuildAsync();
        var (q, error) = await w.Rfq.CreateFromPrAsync(Carla, w.Pr.Id, QuotationKind.Purchase, null, null);

        Assert.Null(error);
        Assert.All(q!.Items, i =>
        {
            Assert.Equal(w.Pr.Id, i.SourcePrId);
            Assert.Equal(w.Pr.Number, i.SourcePrNumber);
            Assert.NotNull(i.SourcePrItemId);
        });
        // a SC não pode mais ser alterada depois de entrar em cotação (regressão da janela de alteração)
        var pr = await w.Db.Requisitions.Include(r => r.Items).SingleAsync(r => r.Id == w.Pr.Id);
        var window = await w.Prs.ChangeWindowErrorAsync(pr);
        Assert.NotNull(window);
    }

    // ================= compra dividida entre vários fornecedores (por família) =================

    private sealed record SplitWorld(
        QuotationService Rfq, RequisitionService Prs, SupplierService Sup, AppDbContext Db,
        Supplier Alfa, Supplier Beta, PurchaseRequisition Pr);

    /// <summary>
    /// SC com produtos de DUAS famílias (EPI e FERRAMENTAS) e dois fornecedores convidados:
    /// a Alfa é a melhor em EPI, a Beta é a melhor em FERRAMENTAS.
    /// </summary>
    private static async Task<SplitWorld> BuildSplitAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new AppDbContext(options);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero));
        var catalog = new CatalogService(db, clock);
        var rfq = new QuotationService(db, clock);
        var prs = new RequisitionService(db, new FakeNumbers(), catalog, clock);
        var sup = new SupplierService(db, clock);

        var (alfa, _) = await sup.CreateAsync(Carla.Id, "Alfa LTDA", "Alfa", "12345678000190", null, null);
        var (beta, _) = await sup.CreateAsync(Carla.Id, "Beta LTDA", "Beta", "98765432000110", null, null);
        // vencer exige homologação (SUP-ERR-030); o mundo da compra dividida é o do
        // fornecedor já homologado
        await sup.SetHomologationAsync(alfa!.Id, SupplierHomologation.Homologado);
        await sup.SetHomologationAsync(beta!.Id, SupplierHomologation.Homologado);
        var (luva, _) = await catalog.CreateAsync(Gustavo.Id, "EPI-001", "Luva de vaqueta", "EPI", "PAR", 20m);
        var (chave, _) = await catalog.CreateAsync(Gustavo.Id, "FER-001", "Chave de fenda 1/4", "FERRAMENTAS", "UN", 30m);

        var (pr, _) = await prs.CreateAsync(Ana, "Reposição de EPI e ferramentas", "CC-01", "NORMAL", null,
            [new ItemInput("", 10, null, null, null, luva!.Id), new ItemInput("", 5, null, null, null, chave!.Id)],
            "CATALOGO");
        await prs.SubmitAsync(Ana, pr!.Id);
        await prs.ApproveAsync(Bruno, pr.Id, null);
        return new SplitWorld(rfq, prs, sup, db, alfa!, beta!, pr);
    }

    /// <summary>Cotação aberta, propostas registradas e processo em análise.</summary>
    private static async Task<Quotation> SplitUpToAnalysisAsync(SplitWorld w)
    {
        var (q, e1) = await w.Rfq.CreateFromPrAsync(Carla, w.Pr.Id, QuotationKind.Purchase, null, null);
        Assert.Null(e1);
        await w.Rfq.InviteSuppliersAsync(Carla, q!.Id, [w.Alfa.Id, w.Beta.Id]);
        var epi = q.Items.Single(i => i.Family == "EPI");
        var fer = q.Items.Single(i => i.Family == "FERRAMENTAS");

        // Alfa: 200 em EPI + 200 em ferramentas + 40 de frete = 440
        await w.Rfq.SubmitProposalAsync(q.Id, w.Alfa.Id, new ProposalInput(10, "30 dias", 40m, null, null,
            [new ProposalItemInput(epi.Id, 20m, null), new ProposalItemInput(fer.Id, 40m, null)]), "PORTAL", "Alfa");
        // Beta: 250 em EPI + 150 em ferramentas, sem frete = 400
        await w.Rfq.SubmitProposalAsync(q.Id, w.Beta.Id, new ProposalInput(12, "28 dias", null, null, null,
            [new ProposalItemInput(epi.Id, 25m, null), new ProposalItemInput(fer.Id, 30m, null)]), "PORTAL", "Beta");
        var (analise, e2) = await w.Rfq.CloseForAnalysisAsync(Carla, q.Id);
        Assert.Null(e2);
        return analise!;
    }

    /// <summary>A família do item vem do catálogo e é o lote da adjudicação.</summary>
    [Fact]
    public async Task Itens_da_cotacao_carregam_a_familia_do_catalogo()
    {
        var w = await BuildSplitAsync();
        var q = await SplitUpToAnalysisAsync(w);
        Assert.Equal(["EPI", "FERRAMENTAS"], q.Families.OrderBy(f => f).ToArray());
        Assert.All(q.Items, i => Assert.NotEqual(string.Empty, i.Family));
    }

    /// <summary>
    /// O pedido do cliente: uma SC com várias famílias vira UMA compra, mas cada família pode
    /// ficar com um fornecedor diferente. O rateio de frete acompanha a fatia ganha.
    /// </summary>
    [Fact]
    public async Task Compra_de_varias_familias_pode_ser_dividida_entre_fornecedores()
    {
        var w = await BuildSplitAsync();
        var q = await SplitUpToAnalysisAsync(w);
        var alfa = q.Proposals.Single(p => p.SupplierId == w.Alfa.Id);
        var beta = q.Proposals.Single(p => p.SupplierId == w.Beta.Id);

        var (dividida, error) = await w.Rfq.AwardByFamilyAsync(Carla, q.Id, [
            new AwardInput("EPI", alfa.Id, "Preço", "Alfa tem o menor preço em EPI."),
            new AwardInput("FERRAMENTAS", beta.Id, "Preço", "Beta tem o menor preço em ferramentas."),
        ]);
        Assert.Null(error);
        Assert.True(dividida!.IsSplitAward);
        Assert.Equal(QuotationStatus.AwaitingManager, dividida.Status);
        Assert.Equal(2, dividida.AwardList.Count);

        // EPI com a Alfa: 200 de itens + metade do frete (o valor dos itens é metade da proposta)
        var epi = dividida.AwardList.Single(a => a.Family == "EPI");
        Assert.Equal(w.Alfa.Id, epi.SupplierId);
        Assert.Equal(200m, epi.ItemsValue);
        Assert.Equal(220m, epi.TotalValue);
        // Ferramentas com a Beta: 150, sem frete na proposta
        var fer = dividida.AwardList.Single(a => a.Family == "FERRAMENTAS");
        Assert.Equal(w.Beta.Id, fer.SupplierId);
        Assert.Equal(150m, fer.TotalValue);
        // dividir sai mais barato do que dar tudo para o melhor total (Beta, 400)
        Assert.Equal(370m, dividida.AwardList.Sum(a => a.TotalValue));

        var timeline = await w.Rfq.TimelineAsync(q.Id);
        var evento = Assert.Single(timeline.Where(e => e.EventType == "COMPRA_DIVIDIDA"));
        Assert.Contains("EPI → Alfa", evento.Description);
        Assert.Contains("FERRAMENTAS → Beta", evento.Description);
    }

    /// <summary>Nenhuma família pode ficar órfã: sem vencedor, o processo não avança.</summary>
    [Fact]
    public async Task Adjudicacao_exige_vencedor_para_cada_familia()
    {
        var w = await BuildSplitAsync();
        var q = await SplitUpToAnalysisAsync(w);
        var alfa = q.Proposals.Single(p => p.SupplierId == w.Alfa.Id);

        var (nada, faltando) = await w.Rfq.AwardByFamilyAsync(Carla, q.Id,
            [new AwardInput("EPI", alfa.Id, null, "Só o EPI.")]);
        Assert.Null(nada);
        Assert.Equal("RFQ-ERR-023", faltando!.Code);
        Assert.Contains("FERRAMENTAS", faltando.Message);

        var (semFamilia, intrusa) = await w.Rfq.AwardByFamilyAsync(Carla, q.Id, [
            new AwardInput("EPI", alfa.Id, null, "ok"),
            new AwardInput("FERRAMENTAS", alfa.Id, null, "ok"),
            new AwardInput("QUIMICOS", alfa.Id, null, "família que não existe"),
        ]);
        Assert.Null(semFamilia);
        Assert.Equal("RFQ-ERR-023", intrusa!.Code);

        var (semJustificativa, erroJust) = await w.Rfq.AwardByFamilyAsync(Carla, q.Id, [
            new AwardInput("EPI", alfa.Id, null, "ok"),
            new AwardInput("FERRAMENTAS", alfa.Id, null, "  "),
        ]);
        Assert.Null(semJustificativa);
        Assert.Equal("RFQ-ERR-021", erroJust!.Code);
        Assert.Contains("FERRAMENTAS", erroJust.Message);

        // recusa não deixa rastro: o processo continua em análise, sem adjudicação
        var intacta = await w.Rfq.GetAsync(q.Id);
        Assert.Equal(QuotationStatus.Analysis, intacta!.Status);
        Assert.Empty(intacta.AwardList);
    }

    /// <summary>Só leva a família quem cotou a família inteira — meia cotação não vira O.C.</summary>
    [Fact]
    public async Task Familia_so_e_adjudicada_a_quem_cotou_a_familia_inteira()
    {
        var w = await BuildSplitAsync();
        var (q, _) = await w.Rfq.CreateFromPrAsync(Carla, w.Pr.Id, QuotationKind.Purchase, null, null);
        await w.Rfq.InviteSuppliersAsync(Carla, q!.Id, [w.Alfa.Id, w.Beta.Id]);
        var epi = q.Items.Single(i => i.Family == "EPI");
        var fer = q.Items.Single(i => i.Family == "FERRAMENTAS");
        await w.Rfq.SubmitProposalAsync(q.Id, w.Alfa.Id, new ProposalInput(10, "30 dias", null, null, null,
            [new ProposalItemInput(epi.Id, 20m, null), new ProposalItemInput(fer.Id, 40m, null)]), "PORTAL", "Alfa");
        // a Beta só cotou o EPI
        var (parcial, _) = await w.Rfq.SubmitProposalAsync(q.Id, w.Beta.Id, new ProposalInput(12, "28 dias", null, null, null,
            [new ProposalItemInput(epi.Id, 18m, null)]), "PORTAL", "Beta");
        await w.Rfq.CloseForAnalysisAsync(Carla, q.Id);
        var propostaAlfa = (await w.Rfq.GetAsync(q.Id))!.Proposals.Single(p => p.SupplierId == w.Alfa.Id);

        var (nada, error) = await w.Rfq.AwardByFamilyAsync(Carla, q.Id, [
            new AwardInput("EPI", parcial!.Id, null, "menor preço"),
            new AwardInput("FERRAMENTAS", parcial.Id, null, "não cotou"),
        ]);
        Assert.Null(nada);
        Assert.Equal("RFQ-ERR-024", error!.Code);
        Assert.Contains("FERRAMENTAS", error.Message);

        // a Beta pode, sim, levar o EPI que cotou inteiro
        var (ok, semErro) = await w.Rfq.AwardByFamilyAsync(Carla, q.Id, [
            new AwardInput("EPI", parcial.Id, null, "menor preço no EPI"),
            new AwardInput("FERRAMENTAS", propostaAlfa.Id, null, "única cotação de ferramentas"),
        ]);
        Assert.Null(semErro);
        Assert.True(ok!.IsSplitAward);

        // mapa por família: a Beta aparece incompleta em ferramentas e não pode ser escolhida lá
        var mapa = w.Rfq.FamilyMap(ok);
        var ferramentas = mapa.Single(l => l.Family == "FERRAMENTAS");
        Assert.DoesNotContain(ferramentas.Offers, o => o.SupplierId == w.Beta.Id);
        var lote = mapa.Single(l => l.Family == "EPI");
        Assert.True(lote.Offers.Single(o => o.SupplierId == w.Beta.Id).Cheapest);
    }

    /// <summary>
    /// O mapa por família diz quem pode mesmo vencer. Fornecedor não homologado continua na
    /// lista — participar da cotação ele pode —, mas marcado como impedido e sem o destaque de
    /// mais barato, que passa para quem pode levar a família. É o que permite à tela avisar
    /// antes: hoje o comprador escolhe, escreve a justificativa e só então leva SUP-ERR-030.
    /// </summary>
    [Fact]
    public async Task Mapa_de_familias_diz_quem_pode_vencer_antes_da_escolha()
    {
        var w = await BuildSplitAsync();
        var q = await SplitUpToAnalysisAsync(w);

        // com todos homologados, a Beta é a mais barata em ferramentas (150 contra 220 da Alfa)
        var antes = (await w.Rfq.FamilyMapAsync(q)).Single(l => l.Family == "FERRAMENTAS");
        Assert.True(antes.Offers.Single(o => o.SupplierId == w.Beta.Id).CanWin);
        Assert.True(antes.Offers.Single(o => o.SupplierId == w.Beta.Id).Cheapest);

        await w.Sup.SetHomologationAsync(w.Beta.Id, SupplierHomologation.EmHomologacao);

        var lote = (await w.Rfq.FamilyMapAsync(q)).Single(l => l.Family == "FERRAMENTAS");
        var beta = lote.Offers.Single(o => o.SupplierId == w.Beta.Id);
        Assert.True(beta.Complete);                 // cotou a família inteira
        Assert.False(beta.CanWin);                  // mas não está homologada
        Assert.False(beta.Cheapest);                // e não é a "melhor oferta" de nada
        Assert.Equal(SupplierHomologation.EmHomologacao, beta.Homologation);
        // o destaque vai para quem pode levar a família, e ela vem primeiro na lista
        Assert.True(lote.Offers.Single(o => o.SupplierId == w.Alfa.Id).Cheapest);
        Assert.Equal(w.Alfa.Id, lote.Offers[0].SupplierId);

        // o mapa e a regra dizem a mesma coisa: escolher a Beta é recusado
        var alfa = q.Proposals.Single(p => p.SupplierId == w.Alfa.Id);
        var betaProposta = q.Proposals.Single(p => p.SupplierId == w.Beta.Id);
        var (nada, erro) = await w.Rfq.AwardByFamilyAsync(Carla, q.Id, [
            new AwardInput("EPI", alfa.Id, null, "Alfa em EPI."),
            new AwardInput("FERRAMENTAS", betaProposta.Id, null, "Beta em ferramentas."),
        ]);
        Assert.Null(nada);
        Assert.Equal("SUP-ERR-030", erro!.Code);
    }

    /// <summary>
    /// Fornecedor inativado depois de propor também não leva a família (RFQ-ERR-040) — o mapa
    /// mostra isso pela situação, não deixa a tela oferecê-lo.
    /// </summary>
    [Fact]
    public async Task Mapa_de_familias_marca_fornecedor_inativo_como_impedido()
    {
        var w = await BuildSplitAsync();
        var q = await SplitUpToAnalysisAsync(w);
        await w.Sup.UpdateAsync(w.Beta.Id, null, null, null, false);

        var lote = (await w.Rfq.FamilyMapAsync(q)).Single(l => l.Family == "FERRAMENTAS");
        var beta = lote.Offers.Single(o => o.SupplierId == w.Beta.Id);
        Assert.False(beta.Active);
        Assert.False(beta.CanWin);
    }

    /// <summary>
    /// A compra dividida vira uma O.C. POR FORNECEDOR: o processo só encerra quando todas
    /// estiverem registradas no SENIOR, e cada uma leva apenas os itens da sua família.
    /// </summary>
    [Fact]
    public async Task Compra_dividida_gera_uma_OC_para_cada_fornecedor()
    {
        var w = await BuildSplitAsync();
        var q = await SplitUpToAnalysisAsync(w);
        var alfa = q.Proposals.Single(p => p.SupplierId == w.Alfa.Id);
        var beta = q.Proposals.Single(p => p.SupplierId == w.Beta.Id);
        await w.Rfq.AwardByFamilyAsync(Carla, q.Id, [
            new AwardInput("EPI", alfa.Id, "Preço", "Alfa é a melhor em EPI."),
            new AwardInput("FERRAMENTAS", beta.Id, "Preço", "Beta é a melhor em ferramentas."),
        ]);
        await w.Rfq.ManagerDecisionAsync(Gustavo, q.Id, "APROVAR", null);
        var (aprovada, _) = await w.Rfq.DirectorDecisionAsync(Diana, q.Id, "APROVAR", null);
        Assert.Equal(QuotationStatus.ApprovedForIssue, aprovada!.Status);

        // com a compra dividida, é preciso dizer de qual fornecedor é a O.C.
        var (semFornecedor, erro) = await w.Rfq.RegisterErpPurchaseOrderAsync(Carla, q.Id, "900", null, null);
        Assert.Null(semFornecedor);
        Assert.Equal("RFQ-ERR-042", erro!.Code);

        var (ocAlfa, e1) = await w.Rfq.RegisterErpPurchaseOrderAsync(
            Carla, q.Id, "900", new DateOnly(2026, 8, 31), null, null, w.Alfa.Id);
        Assert.Null(e1);
        Assert.Equal(220m, ocAlfa!.TotalValue);
        Assert.Equal("EPI", ocAlfa.Families);
        Assert.Equal(20m, ocAlfa.FreightValue);                     // rateio do frete pela fatia
        var itemAlfa = Assert.Single(ocAlfa.Items);
        Assert.Equal("EPI", itemAlfa.Family);
        Assert.Equal(10m, itemAlfa.Quantity);

        // o processo continua aberto: falta a O.C. da Beta
        var meio = await w.Rfq.GetAsync(q.Id);
        Assert.Equal(QuotationStatus.ApprovedForIssue, meio!.Status);
        Assert.Equal("900", meio.AwardList.Single(a => a.Family == "EPI").PurchaseOrderNumber);
        Assert.Null(meio.AwardList.Single(a => a.Family == "FERRAMENTAS").PurchaseOrderId);

        var (ocBeta, e2) = await w.Rfq.RegisterErpPurchaseOrderAsync(
            Carla, q.Id, "901", new DateOnly(2026, 8, 31), null, null, w.Beta.Id);
        Assert.Null(e2);
        Assert.Equal(150m, ocBeta!.TotalValue);
        Assert.Equal("FERRAMENTAS", ocBeta.Families);
        Assert.Equal(w.Beta.Id, ocBeta.SupplierId);

        var fim = await w.Rfq.GetAsync(q.Id);
        Assert.Equal(QuotationStatus.PoIssued, fim!.Status);
        Assert.Equal(ocAlfa.Id, fim.PurchaseOrderId);               // cabeçalho: a primeira O.C.
        Assert.All(fim.AwardList, a => Assert.NotNull(a.PurchaseOrderId));
        Assert.Equal(2, await w.Db.PurchaseOrders.CountAsync(o => o.QuotationId == q.Id));

        // nada sobra para registrar
        var (repetida, esgotado) = await w.Rfq.RegisterErpPurchaseOrderAsync(
            Carla, q.Id, "902", null, null, null, w.Alfa.Id);
        Assert.Null(repetida);
        Assert.Equal("RFQ-ERR-040", esgotado!.Code);
    }

    /// <summary>
    /// Fornecedor único continua sendo uma O.C. só, mesmo com várias famílias: a divisão
    /// depende de quem ganhou, não da quantidade de famílias.
    /// </summary>
    [Fact]
    public async Task Fornecedor_unico_leva_todas_as_familias_em_uma_unica_OC()
    {
        var w = await BuildSplitAsync();
        var q = await SplitUpToAnalysisAsync(w);
        var alfa = q.Proposals.Single(p => p.SupplierId == w.Alfa.Id);

        var (escolhida, error) = await w.Rfq.SelectWinnerAsync(Carla, q.Id, alfa.Id, "Preço", "Melhor pacote fechado.");
        Assert.Null(error);
        Assert.False(escolhida!.IsSplitAward);
        Assert.Equal(2, escolhida.AwardList.Count);                       // uma linha por família…
        Assert.Single(escolhida.AwardedSupplierIds);                      // …mas um fornecedor só
        Assert.Equal(440m, escolhida.AwardList.Sum(a => a.TotalValue));   // proposta cheia, sem sobra de rateio

        await w.Rfq.ManagerDecisionAsync(Gustavo, q.Id, "APROVAR", null);
        await w.Rfq.DirectorDecisionAsync(Diana, q.Id, "APROVAR", null);
        var (oc, e1) = await w.Rfq.RegisterErpPurchaseOrderAsync(Carla, q.Id, "910", new DateOnly(2026, 8, 31), null);
        Assert.Null(e1);
        Assert.Equal(440m, oc!.TotalValue);
        Assert.Equal(2, oc.Items.Count);
        Assert.Equal(40m, oc.FreightValue);
        var fim = await w.Rfq.GetAsync(q.Id);
        Assert.Equal(QuotationStatus.PoIssued, fim!.Status);
        Assert.Single(await w.Db.PurchaseOrders.Where(o => o.QuotationId == q.Id).ToListAsync());
    }

    // ================= separação na origem: um processo por família =================

    /// <summary>
    /// O pedido do cliente: a SC tem EPI e ferramenta, e cada um precisa ir para um processo
    /// diferente. Ao cotar só o EPI, a SC CONTINUA na fila com a ferramenta — o que sobrou não
    /// some junto com o primeiro processo.
    /// </summary>
    [Fact]
    public async Task Sc_com_duas_familias_pode_ser_separada_em_processos_diferentes()
    {
        var w = await BuildSplitAsync();
        var pr = await w.Db.Requisitions.Include(r => r.Items).SingleAsync(r => r.Id == w.Pr.Id);
        var luva = pr.Items.Single(i => i.Description.Contains("Luva"));
        var chave = pr.Items.Single(i => i.Description.Contains("Chave"));

        // a fila mostra a SC com os dois itens e as duas famílias
        var (fila, _) = await w.Rfq.QueueAsync();
        var antes = Assert.Single(fila, e => e.Pr.Id == pr.Id);
        Assert.Equal(2, antes.Pending.Count);
        Assert.False(antes.Partial);
        Assert.Equal(["EPI", "FERRAMENTAS"], antes.Pending.Select(i => i.Family).OrderBy(f => f).ToArray());

        // processo só do EPI
        var (epi, e1) = await w.Rfq.CreateFromItemsAsync(Carla, [luva.Id], QuotationKind.Purchase, null, null);
        Assert.Null(e1);
        Assert.Equal("EPI", Assert.Single(epi!.Items).Family);

        // a SC continua na fila, agora só com a ferramenta
        (fila, _) = await w.Rfq.QueueAsync();
        var depois = Assert.Single(fila, e => e.Pr.Id == pr.Id);
        var pendente = Assert.Single(depois.Pending);
        Assert.Equal("FERRAMENTAS", pendente.Family);
        Assert.True(depois.Partial);

        // o item já cotado não entra em outro processo
        var (repetido, e2) = await w.Rfq.CreateFromItemsAsync(Carla, [luva.Id], QuotationKind.Purchase, null, null);
        Assert.Null(repetido);
        Assert.Equal("RFQ-ERR-062", e2!.Code);

        // segundo processo com o que sobrou: a SC sai da fila
        var (fer, e3) = await w.Rfq.CreateFromItemsAsync(Carla, [chave.Id], QuotationKind.Purchase, null, null);
        Assert.Null(e3);
        Assert.Equal("FERRAMENTAS", Assert.Single(fer!.Items).Family);
        Assert.NotEqual(epi.Id, fer.Id);
        (fila, _) = await w.Rfq.QueueAsync();
        Assert.DoesNotContain(fila, e => e.Pr.Id == pr.Id);
    }

    /// <summary>"Abrir tudo" depois de uma separação leva apenas os itens que sobraram.</summary>
    [Fact]
    public async Task Abrir_a_sc_inteira_leva_so_os_itens_ainda_sem_processo()
    {
        var w = await BuildSplitAsync();
        var pr = await w.Db.Requisitions.Include(r => r.Items).SingleAsync(r => r.Id == w.Pr.Id);
        var luva = pr.Items.Single(i => i.Description.Contains("Luva"));

        await w.Rfq.CreateFromItemsAsync(Carla, [luva.Id], QuotationKind.Purchase, null, null);
        var (resto, error) = await w.Rfq.CreateFromPrAsync(Carla, pr.Id, QuotationKind.Purchase, null, null);
        Assert.Null(error);
        var item = Assert.Single(resto!.Items);
        Assert.Equal("FERRAMENTAS", item.Family);

        // sem itens livres, a SC não abre um terceiro processo
        var (nada, esgotada) = await w.Rfq.CreateFromPrAsync(Carla, pr.Id, QuotationKind.Purchase, null, null);
        Assert.Null(nada);
        Assert.Equal("RFQ-ERR-001", esgotada!.Code);
    }

    /// <summary>
    /// Com a SC separada, a Gestão de Solicitações mostra a situação POR ITEM: um item pode estar
    /// em cotação enquanto o outro ainda espera o comprador.
    /// </summary>
    [Fact]
    public async Task Gestao_de_solicitacoes_mostra_a_situacao_de_cada_item_quando_a_sc_e_separada()
    {
        var w = await BuildSplitAsync();
        var pr = await w.Db.Requisitions.Include(r => r.Items).SingleAsync(r => r.Id == w.Pr.Id);
        var luva = pr.Items.Single(i => i.Description.Contains("Luva"));
        var chave = pr.Items.Single(i => i.Description.Contains("Chave"));
        var (epi, _) = await w.Rfq.CreateFromItemsAsync(Carla, [luva.Id], QuotationKind.Purchase, null, null);

        var triagem = new TriageService(w.Db, new FixedTimeProvider(new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero)));
        var ticket = Assert.Single((await triagem.ListAsync(Carla, "TODAS")).Where(t => t.Id == pr.Id));
        Assert.True(ticket.SplitProcesses);

        var itemEpi = ticket.Items!.Single(i => i.Id == luva.Id);
        Assert.Equal("EPI", itemEpi.Family);
        Assert.Equal(epi!.Number, itemEpi.QuotationNumber);
        Assert.Equal(ProcessStatus.InQuotation.Key, itemEpi.Process!.Key);

        var itemFer = ticket.Items!.Single(i => i.Id == chave.Id);
        Assert.Null(itemFer.QuotationNumber);
        Assert.Equal(ProcessStatus.Pending.Key, itemFer.Process!.Key);
    }
}
