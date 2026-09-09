using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// A outra metade da situação: <b>de quem</b> a linha está esperando, e <b>há quanto tempo</b>.
///
/// "Aguardando aprovação" diz a etapa e manda o comprador abrir o centro de custo para
/// descobrir quem aprova; "em cotação" manda abrir o processo para cruzar convidados com
/// propostas. O sistema já sabia as duas coisas — o que estes testes protegem é ele
/// continuar dizendo, e nunca datar a espera por um relógio que não é o dela.
/// </summary>
public class EsperaDaTorreTests
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

    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana", Roles.Requester);
    private static readonly Actor Bruno = new(Guid.NewGuid(), "Bruno", Roles.Approver);
    private static readonly Actor Carla = new(Guid.NewGuid(), "Carla Compradora", Roles.PurchasingOfficer);
    private static readonly DateTimeOffset Dia1 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed record Mundo(
        AppDbContext Db, TorreDeControleService Torre, RequisitionService Prs,
        QuotationService Rfq, SupplierService Sup, RelogioFixo Relogio);

    private static Mundo Build()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var relogio = new RelogioFixo(Dia1);
        return new Mundo(db, new TorreDeControleService(db, relogio),
            new RequisitionService(db, new NumerosFalsos(), new CatalogService(db, relogio), relogio),
            new QuotationService(db, relogio), new SupplierService(db, relogio), relogio);
    }

    private static async Task<PurchaseRequisition> ScAprovadaAsync(Mundo w)
    {
        var (pr, _) = await w.Prs.CreateAsync(Ana, "Reposição", "CC-01", "NORMAL", null,
            [new ItemInput("Martelete", 1, "UN", 100, null)]);
        await w.Prs.SubmitAsync(Ana, pr!.Id);
        await w.Prs.ApproveAsync(Bruno, pr.Id, null);
        return pr;
    }

    private static async Task<Supplier> FornecedorAsync(Mundo w, string nome, string cnpj)
    {
        var (s, _) = await w.Sup.CreateAsync(Carla.Id, $"{nome} LTDA", nome, cnpj, null, "81 3333-1000");
        await w.Sup.SetHomologationAsync(s!.Id, SupplierHomologation.Homologado);
        return s;
    }

    /// <summary>Dá ao centro CC-01 os aprovadores dos dois níveis, e devolve os dois atores.</summary>
    private static async Task<(Actor N1, Actor N2)> ComAlcadasAsync(Mundo w)
    {
        var marcos = new Actor(Guid.NewGuid(), "Marcos Gerente", Roles.Approver);
        var paula = new Actor(Guid.NewGuid(), "Paula Diretora", Roles.Approver);
        var centro = new CostCenter { Code = "CC-01", Name = "Manutenção", Active = true };
        centro.Approvers.Add(new CostCenterApprover
        { CostCenterId = centro.Id, UserId = marcos.Id, UserName = marcos.Label, Level = 1 });
        centro.Approvers.Add(new CostCenterApprover
        { CostCenterId = centro.Id, UserId = paula.Id, UserName = paula.Label, Level = 2 });
        w.Db.CostCenters.Add(centro);
        await w.Db.SaveChangesAsync();
        return (marcos, paula);
    }

    private static async Task<EsperaDaLinha?> EsperaAsync(Mundo w) =>
        (await w.Torre.ConsultarAsync(new FiltroTorre())).Items.Single().WaitingOn;

    [Fact]
    public async Task Sem_comprador_a_espera_e_da_triagem()
    {
        var w = Build();
        await ScAprovadaAsync(w);

        var espera = await EsperaAsync(w);

        Assert.Equal("Triagem — atribuir comprador", espera!.Who);
        Assert.Equal(0, espera.Days);
    }

    [Fact]
    public async Task Em_cotacao_a_espera_nomeia_quem_nao_entregou()
    {
        // é a pergunta "quem não entregou?" respondida na linha, sem abrir o processo
        // para cruzar a lista de convidados com a de propostas
        var w = Build();
        var pr = await ScAprovadaAsync(w);
        var alfa = await FornecedorAsync(w, "Alfa", "12345678000190");
        var beta = await FornecedorAsync(w, "Beta", "98765432000110");
        var (q, _) = await w.Rfq.CreateFromPrAsync(Carla, pr.Id, QuotationKind.Purchase, null, null);
        await w.Rfq.InviteSuppliersAsync(Carla, q!.Id, [alfa.Id, beta.Id], default, new DateOnly(2026, 9, 5));
        await w.Rfq.SubmitProposalAsync(q.Id, alfa.Id,
            new(10, "30 dias", 0, null, null, [new ProposalItemInput(q.Items.Single().Id, 90m, null)]),
            "PORTAL", "Alfa");

        var espera = await EsperaAsync(w);

        Assert.Equal("Proposta — Beta", espera!.Who);   // Alfa já entregou: não se cobra dela
    }

    [Fact]
    public async Task O_atraso_do_fornecedor_vira_o_detalhe_da_linha()
    {
        var w = Build();
        var pr = await ScAprovadaAsync(w);
        var alfa = await FornecedorAsync(w, "Alfa", "12345678000190");
        var (q, _) = await w.Rfq.CreateFromPrAsync(Carla, pr.Id, QuotationKind.Purchase, null, null);
        await w.Rfq.InviteSuppliersAsync(Carla, q!.Id, [alfa.Id], default, new DateOnly(2026, 9, 3));
        w.Relogio.Agora = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

        var espera = await EsperaAsync(w);

        Assert.Equal("Proposta — Alfa", espera!.Who);
        Assert.Contains("5 dia", espera.Detail);
        Assert.Equal(7, espera.Days);   // conta do convite, não do vencimento do prazo
    }

    [Fact]
    public async Task Respondidos_todos_a_bola_volta_ao_comprador()
    {
        // dizer "aguardando fornecedor" aqui mandaria cobrar quem já entregou
        var w = Build();
        var pr = await ScAprovadaAsync(w);
        var alfa = await FornecedorAsync(w, "Alfa", "12345678000190");
        var (q, _) = await w.Rfq.CreateFromPrAsync(Carla, pr.Id, QuotationKind.Purchase, null, null);
        await w.Rfq.InviteSuppliersAsync(Carla, q!.Id, [alfa.Id]);
        await w.Rfq.SubmitProposalAsync(q.Id, alfa.Id,
            new(10, "30 dias", 0, null, null, [new ProposalItemInput(q.Items.Single().Id, 90m, null)]),
            "PORTAL", "Alfa");

        var espera = await EsperaAsync(w);

        Assert.Equal("Análise das propostas — comprador", espera!.Who);
    }

    [Fact]
    public async Task Fornecedor_dispensado_sai_da_cobranca()
    {
        // o processo já decidiu seguir sem ele: continuar cobrando seria pedir uma ação
        // que ninguém vai tomar
        var w = Build();
        var pr = await ScAprovadaAsync(w);
        var alfa = await FornecedorAsync(w, "Alfa", "12345678000190");
        var beta = await FornecedorAsync(w, "Beta", "98765432000110");
        var (q, _) = await w.Rfq.CreateFromPrAsync(Carla, pr.Id, QuotationKind.Purchase, null, null);
        await w.Rfq.InviteSuppliersAsync(Carla, q!.Id, [alfa.Id, beta.Id], default, new DateOnly(2026, 9, 3));
        await w.Rfq.DispensarConviteAsync(Carla, q.Id, beta.Id, "Avisou que nao vai cotar desta vez");
        w.Relogio.Agora = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

        var espera = await EsperaAsync(w);

        Assert.Equal("Proposta — Alfa", espera!.Who);
        Assert.DoesNotContain("Beta", espera.Who);
    }

    [Fact]
    public async Task Aguardando_aprovacao_diz_o_nivel_e_o_nome_de_quem_decide()
    {
        var w = Build();
        await ComAlcadasAsync(w);
        var pr = await ScAprovadaAsync(w);
        var alfa = await FornecedorAsync(w, "Alfa", "12345678000190");
        var (q, _) = await w.Rfq.CreateFromPrAsync(Carla, pr.Id, QuotationKind.Purchase, null, null);
        await w.Rfq.InviteSuppliersAsync(Carla, q!.Id, [alfa.Id]);
        await w.Rfq.SubmitProposalAsync(q.Id, alfa.Id,
            new(10, "30 dias", 0, null, null, [new ProposalItemInput(q.Items.Single().Id, 90m, null)]),
            "PORTAL", "Alfa");
        await w.Rfq.CloseForAnalysisAsync(Carla, q.Id);
        var atual = await w.Rfq.GetAsync(q.Id);
        await w.Rfq.AwardByItemAsync(Carla, q.Id,
            [new AwardInput("", atual!.Proposals.Single().Id, "Menor preco", "Unica proposta",
                atual.Items.Single().Id)]);
        // a escolha do vencedor é o que abre a espera do Nível 1; o relógio corre dali
        w.Relogio.Agora = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

        var espera = await EsperaAsync(w);

        Assert.Equal("Aprovação Nível 1 — Marcos Gerente", espera!.Who);
        Assert.Equal(6, espera.Days);
    }

    [Fact]
    public async Task Passado_o_nivel_1_a_espera_e_do_nivel_2_e_o_relogio_reinicia()
    {
        // contar desde a escolha do vencedor cobraria do Nível 2 o tempo do Nível 1
        var w = Build();
        var (marcos, _) = await ComAlcadasAsync(w);
        var pr = await ScAprovadaAsync(w);
        var alfa = await FornecedorAsync(w, "Alfa", "12345678000190");
        var (q, _) = await w.Rfq.CreateFromPrAsync(Carla, pr.Id, QuotationKind.Purchase, null, null);
        await w.Rfq.InviteSuppliersAsync(Carla, q!.Id, [alfa.Id]);
        await w.Rfq.SubmitProposalAsync(q.Id, alfa.Id,
            new(10, "30 dias", 0, null, null, [new ProposalItemInput(q.Items.Single().Id, 90m, null)]),
            "PORTAL", "Alfa");
        await w.Rfq.CloseForAnalysisAsync(Carla, q.Id);
        var atual = await w.Rfq.GetAsync(q.Id);
        await w.Rfq.AwardByItemAsync(Carla, q.Id,
            [new AwardInput("", atual!.Proposals.Single().Id, "Menor preco", "Unica proposta",
                atual.Items.Single().Id)]);

        w.Relogio.Agora = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        var (depois, erro) = await w.Rfq.ManagerDecisionAsync(marcos, q.Id, "APROVAR", null);
        Assert.Null(erro);
        Assert.Equal(QuotationStatus.AwaitingDirector, depois!.Status);
        w.Relogio.Agora = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

        var espera = await EsperaAsync(w);

        Assert.Equal("Aprovação Nível 2 — Paula Diretora", espera!.Who);
        Assert.Equal(2, espera.Days);   // desde a aprovação do Nível 1, não desde a escolha
    }

    [Fact]
    public async Task Centro_sem_aprovador_cadastrado_diz_isso_em_vez_de_um_nivel_sem_dono()
    {
        // fila parada porque ninguém pode aprovar é o defeito que precisa aparecer;
        // esconder o vazio o deixaria passando por demora normal
        var w = Build();
        var pr = await ScAprovadaAsync(w);
        var alfa = await FornecedorAsync(w, "Alfa", "12345678000190");
        var (q, _) = await w.Rfq.CreateFromPrAsync(Carla, pr.Id, QuotationKind.Purchase, null, null);
        await w.Rfq.InviteSuppliersAsync(Carla, q!.Id, [alfa.Id]);
        await w.Rfq.SubmitProposalAsync(q.Id, alfa.Id,
            new(10, "30 dias", 0, null, null, [new ProposalItemInput(q.Items.Single().Id, 90m, null)]),
            "PORTAL", "Alfa");
        await w.Rfq.CloseForAnalysisAsync(Carla, q.Id);
        var atual = await w.Rfq.GetAsync(q.Id);
        await w.Rfq.AwardByItemAsync(Carla, q.Id,
            [new AwardInput("", atual!.Proposals.Single().Id, "Menor preco", "Unica proposta",
                atual.Items.Single().Id)]);

        var espera = await EsperaAsync(w);

        Assert.Contains("sem aprovador cadastrado", espera!.Who);
    }

    [Fact]
    public void Sem_marca_de_entrada_a_espera_fica_sem_data_em_vez_de_chutar()
    {
        // usar a criação da SC contaria como espera um tempo em que a etapa nem existia
        var sc = new PurchaseRequisition
        {
            Number = "PR-2026-000001", CostCenter = "CC-01", RequesterLabel = "Ana",
            Status = RequisitionStatus.Submitted, SubmittedAt = null,
            CreatedAt = Dia1.AddDays(-30),
        };

        var espera = TorreDeControleService.EsperaDe(
            sc, null, null, AlcadasDoCentro.Nenhuma, new DateOnly(2026, 9, 1), Dia1);

        Assert.Equal("Aprovação da solicitação", espera!.Who);
        Assert.Null(espera.Since);
        Assert.Null(espera.Days);
    }

    [Fact]
    public async Task Item_entregue_nao_espera_mais_ninguem()
    {
        var w = Build();
        await ScAprovadaAsync(w);
        var linha = (await w.Torre.ConsultarAsync(new FiltroTorre())).Items.Single();
        Assert.NotNull(linha.WaitingOn);   // enquanto está aberto, espera alguém

        var pedido = new PurchaseOrder
        {
            Number = "PO-2026-000001", SourcePrId = linha.RequisitionId, SupplierId = Guid.NewGuid(),
            SupplierName = "Alfa", Status = PurchaseOrderStatus.Received, CreatedAt = Dia1,
        };
        w.Db.PurchaseOrders.Add(pedido);
        await w.Db.SaveChangesAsync();

        Assert.Null((await w.Torre.ConsultarAsync(new FiltroTorre())).Items.Single().WaitingOn);
    }

    // ---- cada card leva à lista que ele contou -----------------------------

    /// <summary>Deixa o item com O.C. emitida, com ou sem nota fiscal lançada.</summary>
    private static async Task<Guid> ComPedidoAsync(Mundo w, bool comNota)
    {
        var pr = await ScAprovadaAsync(w);
        var pedido = new PurchaseOrder
        {
            Number = $"PO-2026-{Guid.NewGuid().ToString()[..6]}", SourcePrId = pr.Id,
            SupplierId = Guid.NewGuid(), SupplierName = "Alfa",
            Status = PurchaseOrderStatus.Issued, CreatedAt = Dia1,
        };
        if (comNota)
            pedido.Invoices.Add(new PurchaseOrderInvoice
            {
                OrderId = pedido.Id, Number = "NF-1",
                IssuedOn = new DateOnly(2026, 9, 2), CreatedAt = Dia1.AddDays(1),
            });
        w.Db.PurchaseOrders.Add(pedido);
        await w.Db.SaveChangesAsync();
        return pr.Id;
    }

    [Fact]
    public async Task Em_faturamento_e_aguardando_recebimento_abrem_listas_diferentes()
    {
        // os dois números do topo eram contados em separado e caíam no mesmo filtro:
        // clicar em "Em faturamento: 1" mostrava as duas linhas, e o card mentia sobre
        // a própria lista
        var w = Build();
        var semNota = await ComPedidoAsync(w, comNota: false);
        var comNota = await ComPedidoAsync(w, comNota: true);

        var kpis = (await w.Torre.ConsultarAsync(new FiltroTorre())).Kpis;
        Assert.Equal(1, kpis.EmFaturamento);
        Assert.Equal(1, kpis.AguardandoRecebimento);

        var faturando = await w.Torre.ConsultarAsync(new FiltroTorre(Stage: "RECEBIMENTO", Invoicing: true));
        var recebendo = await w.Torre.ConsultarAsync(new FiltroTorre(Stage: "RECEBIMENTO", Invoicing: false));

        Assert.Equal(kpis.EmFaturamento, faturando.Total);
        Assert.Equal(kpis.AguardandoRecebimento, recebendo.Total);
        Assert.Equal(semNota, faturando.Items.Single().RequisitionId);
        Assert.Equal(comNota, recebendo.Items.Single().RequisitionId);
    }

    [Fact]
    public async Task Sem_o_recorte_o_recebimento_continua_trazendo_as_duas_filas()
    {
        // quem filtra só pela etapa quer as duas: o recorte é opcional, não obrigatório
        var w = Build();
        await ComPedidoAsync(w, comNota: false);
        await ComPedidoAsync(w, comNota: true);

        var pagina = await w.Torre.ConsultarAsync(new FiltroTorre(Stage: "RECEBIMENTO"));

        Assert.Equal(2, pagina.Total);
    }

    [Fact]
    public async Task O_numero_de_precisa_de_voce_e_o_tamanho_da_lista_que_ele_abre()
    {
        // somar "novos + em cotação + aguardando O.C." dava um número parecido e errado:
        // deixava de fora a exceção em etapa de recebimento, que volta ao comprador
        var w = Build();
        await ScAprovadaAsync(w);                       // novo, sem comprador: é do comprador
        var comExcecao = await ComPedidoAsync(w, comNota: true);
        var pedido = w.Db.PurchaseOrders.Single(o => o.SourcePrId == comExcecao);
        pedido.NoErpReason = "Fechado sem O.C. do ERP por indisponibilidade do sistema";
        await w.Db.SaveChangesAsync();

        var kpis = (await w.Torre.ConsultarAsync(new FiltroTorre())).Kpis;
        var fila = await w.Torre.ConsultarAsync(new FiltroTorre(NeedsBuyer: true));

        Assert.Equal(2, kpis.PrecisaDeVoce);            // o novo e a exceção
        Assert.Equal(kpis.PrecisaDeVoce, fila.Total);
        // a soma antiga daria 1: a exceção está em etapa de recebimento
        Assert.Equal(1, kpis.Novos + kpis.EmCotacao + kpis.AguardandoOc);
    }

    [Fact]
    public async Task O_prazo_da_etapa_julga_a_mesma_espera_que_a_linha_mostra()
    {
        // duas contas para "estourou?" dariam uma linha dizendo 8 dias e um veredito
        // calculado sobre outro número
        var w = Build();
        await ScAprovadaAsync(w);
        w.Db.StageSlas.Add(new StageSla { Stage = "SOLICITACAO", MaxDays = 2 });
        await w.Db.SaveChangesAsync();
        w.Relogio.Agora = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

        var linha = (await w.Torre.ConsultarAsync(new FiltroTorre())).Items.Single();

        Assert.Equal(5, linha.WaitingOn!.Days);
        Assert.Equal(2, linha.Sla!.MaxDays);
        Assert.Equal("ESTOURADO", linha.Sla.Status);
    }

    [Fact]
    public async Task O_card_de_prazo_estourado_abre_a_lista_do_mesmo_tamanho()
    {
        var w = Build();
        await ScAprovadaAsync(w);   // vai estourar: 5 dias parados contra prazo de 2
        w.Db.StageSlas.Add(new StageSla { Stage = "SOLICITACAO", MaxDays = 2 });
        await w.Db.SaveChangesAsync();
        w.Relogio.Agora = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

        var pagina = await w.Torre.ConsultarAsync(new FiltroTorre());
        var estourados = await w.Torre.ConsultarAsync(new FiltroTorre(SlaBreached: true));

        Assert.Equal(1, pagina.Kpis.PrazoEstourado);
        Assert.Equal(pagina.Kpis.PrazoEstourado, estourados.Total);
    }

    [Fact]
    public async Task Dentro_do_prazo_a_linha_nao_vira_estouro()
    {
        var w = Build();
        await ScAprovadaAsync(w);
        w.Db.StageSlas.Add(new StageSla { Stage = "SOLICITACAO", MaxDays = 10 });
        await w.Db.SaveChangesAsync();
        w.Relogio.Agora = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

        var pagina = await w.Torre.ConsultarAsync(new FiltroTorre());

        Assert.Equal("OK", pagina.Items.Single().Sla!.Status);
        Assert.Equal(0, pagina.Kpis.PrazoEstourado);
    }
}
