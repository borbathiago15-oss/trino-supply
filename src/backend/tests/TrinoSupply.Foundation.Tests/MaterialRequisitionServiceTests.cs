using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Inventory;
using TrinoSupply.Foundation.Api.Materials;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// Fluxo do almoxarifado depois da revisão de 2026-08-26: o solicitante pede, o responsável do
/// centro (Nível 1) aprova (podendo reduzir a quantidade), o estoque atende o que tem e o que
/// faltar vira solicitação de compra no nome de quem pediu. A posição de saldo fica no sistema
/// de almoxarifado da operação — aqui guardamos o atendimento.
/// </summary>
public class MaterialRequisitionServiceTests
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
    private static readonly Actor Bruno = new(Guid.NewGuid(), "Bruno Responsável", Roles.Approver);
    private static readonly Actor Otavio = new(Guid.NewGuid(), "Otávio Almoxarife", Roles.WarehouseOperator);

    private sealed record World(
        MaterialRequisitionService Mrs, RequisitionService Prs, CatalogService Catalog, AppDbContext Db,
        CatalogItem Detergente, CatalogItem Papel);

    private static async Task<World> BuildAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        var catalog = new CatalogService(db, clock);
        var inv = new InventoryService(db, clock);
        var mrs = new MaterialRequisitionService(db, catalog, clock);
        var prs = new RequisitionService(db, new FakeNumbers(), catalog, clock);

        var (detergente, _) = await catalog.CreateAsync(Otavio.Id, "LMP-001", "Detergente neutro 500ml", "MATERIAL DE LIMPEZA", "UN", 3.5m);
        var (papel, _) = await catalog.CreateAsync(Otavio.Id, "ESC-001", "Papel A4 resma 500fl", "MATERIAL DE ESCRITORIO", "PC", 25m);
        return new World(mrs, prs, catalog, db, detergente!, papel!);
    }

    /// <summary>Centro de custo com o responsável do Nível 1 definido.</summary>
    private static async Task ComResponsavelAsync(World w, string code, Actor responsavel)
    {
        w.Db.CostCenters.Add(new CostCenter
        {
            Code = code, Name = "Centro de teste", Active = true,
            ManagerUserId = responsavel.Id, ManagerName = responsavel.Label,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await w.Db.SaveChangesAsync();
    }

    /// <summary>Centro de custo com uma lista de gente no nível informado (alçadas do cadastro).</summary>
    private static async Task ComNivelAsync(World w, string code, int level, params Actor[] pessoas)
    {
        var ccId = await w.Db.CostCenters.Where(c => c.Code == code).Select(c => c.Id).SingleAsync();
        foreach (var pessoa in pessoas)
            w.Db.CostCenterApprovers.Add(new CostCenterApprover
            {
                CostCenterId = ccId, UserId = pessoa.Id, UserName = pessoa.Label,
                Level = level, CreatedAt = DateTimeOffset.UtcNow,
            });
        await w.Db.SaveChangesAsync();
    }

    private static async Task<MaterialRequisition> AprovadaAsync(World w, params MaterialItemInput[] itens)
    {
        var (mr, _) = await w.Mrs.CreateAsync(Ana, "CC-01", null, itens);
        var (aprovada, error) = await w.Mrs.ApproveAsync(Bruno, mr!.Id, null, null);
        Assert.Null(error);
        return aprovada!;
    }

    // ---- criação ------------------------------------------------------------
    [Fact]
    public async Task Criacao_exige_itens_do_catalogo_e_nasce_aguardando_aprovacao()
    {
        var w = await BuildAsync();

        var (mr, error) = await w.Mrs.CreateAsync(Ana, "CC-ADM-01", "Reposição do andar 2",
            [new MaterialItemInput(w.Detergente.Id, 10)]);

        Assert.Null(error);
        Assert.StartsWith("MR-2026-", mr!.Number);
        Assert.Equal(MaterialRequisitionStatus.Submitted, mr.Status);
        Assert.Equal("LMP-001", mr.Items.Single().CatalogCode);
    }

    [Fact]
    public async Task Criacao_sem_itens_ou_sem_centro_de_custo_e_recusada()
    {
        var w = await BuildAsync();

        var (_, semCc) = await w.Mrs.CreateAsync(Ana, " ", null, [new MaterialItemInput(w.Detergente.Id, 1)]);
        var (_, semItens) = await w.Mrs.CreateAsync(Ana, "CC-01", null, []);
        var (_, qtdZero) = await w.Mrs.CreateAsync(Ana, "CC-01", null, [new MaterialItemInput(w.Detergente.Id, 0)]);

        Assert.Equal("MR-ERR-021", semCc!.Code);
        Assert.Equal("MR-ERR-030", semItens!.Code);
        Assert.Equal("MR-ERR-010", qtdZero!.Code);
    }

    [Fact]
    public async Task Item_inativo_do_catalogo_e_recusado()
    {
        var w = await BuildAsync();
        var tracked = await w.Db.CatalogItems.SingleAsync(i => i.Id == w.Detergente.Id);
        tracked.Active = false;
        await w.Db.SaveChangesAsync();

        var (_, error) = await w.Mrs.CreateAsync(Ana, "CC-01", null, [new MaterialItemInput(w.Detergente.Id, 1)]);

        Assert.Equal("PR-ERR-022", error!.Code);
    }

    // ---- aprovação do Nível 1 -----------------------------------------------
    [Fact]
    public async Task Nivel_1_pode_reduzir_a_quantidade_mas_nunca_aumentar()
    {
        var w = await BuildAsync();
        var (mr, _) = await w.Mrs.CreateAsync(Ana, "CC-01", null, [new MaterialItemInput(w.Detergente.Id, 10)]);
        var itemId = mr!.Items.Single().Id;

        var (_, acima) = await w.Mrs.ApproveAsync(Bruno, mr.Id, [new(itemId, 12)], null);
        Assert.Equal("MR-ERR-031", acima!.Code);

        var (aprovada, error) = await w.Mrs.ApproveAsync(Bruno, mr.Id, [new(itemId, 4)], "só 4 por ora");
        Assert.Null(error);
        Assert.Equal(MaterialRequisitionStatus.Approved, aprovada!.Status);
        var item = aprovada.Items.Single();
        Assert.Equal(10m, item.Quantity);            // a pedida não muda
        Assert.Equal(4m, item.ApprovedQuantity);
        Assert.Equal(4m, item.EffectiveQuantity);
        Assert.Equal("Bruno Responsável", aprovada.ApprovedByLabel);
    }

    [Fact]
    public async Task Qualquer_pessoa_do_nivel_1_do_centro_aprova()
    {
        var w = await BuildAsync();
        await ComResponsavelAsync(w, "CC-01", Bruno);
        var carla = new Actor(Guid.NewGuid(), "Carla Nível 1", Roles.Approver);
        var diego = new Actor(Guid.NewGuid(), "Diego Nível 1", Roles.Approver);
        await ComNivelAsync(w, "CC-01", ApprovalLevels.Level1, carla, diego);

        // com a lista cadastrada, o gerente antigo deixa de valer sozinho
        var (mr, _) = await w.Mrs.CreateAsync(Ana, "CC-01", null, [new MaterialItemInput(w.Detergente.Id, 3)]);
        var (_, foraDaAlcada) = await w.Mrs.ApproveAsync(Bruno, mr!.Id, null, null);
        Assert.Equal("MR-ERR-002", foraDaAlcada!.Code);
        Assert.Contains("Carla Nível 1", foraDaAlcada.Message);

        var (aprovada, error) = await w.Mrs.ApproveAsync(diego, mr.Id, null, null);
        Assert.Null(error);
        Assert.Equal(MaterialRequisitionStatus.Approved, aprovada!.Status);
        Assert.Equal("Diego Nível 1", aprovada.ApprovedByLabel);
    }

    [Fact]
    public async Task Aprovacao_e_do_responsavel_do_centro_e_recusa_exige_motivo()
    {
        var w = await BuildAsync();
        await ComResponsavelAsync(w, "CC-01", Bruno);
        var (mr, _) = await w.Mrs.CreateAsync(Ana, "CC-01", null, [new MaterialItemInput(w.Detergente.Id, 3)]);

        var outro = new Actor(Guid.NewGuid(), "Outro Aprovador", Roles.Approver);
        var (_, foraDaAlcada) = await w.Mrs.ApproveAsync(outro, mr!.Id, null, null);
        Assert.Equal("MR-ERR-002", foraDaAlcada!.Code);

        var (_, semMotivo) = await w.Mrs.RejectAsync(Bruno, mr.Id, "  ");
        Assert.Equal("MR-ERR-030", semMotivo!.Code);

        var (recusada, error) = await w.Mrs.RejectAsync(Bruno, mr.Id, "sem verba neste mês");
        Assert.Null(error);
        Assert.Equal(MaterialRequisitionStatus.Rejected, recusada!.Status);
        Assert.Equal("sem verba neste mês", recusada.DecisionReason);
    }

    [Fact]
    public async Task Fila_do_estoque_so_mostra_o_que_o_nivel_1_aprovou()
    {
        var w = await BuildAsync();
        await w.Mrs.CreateAsync(Ana, "CC-01", null, [new MaterialItemInput(w.Detergente.Id, 1)]);
        await AprovadaAsync(w, new MaterialItemInput(w.Papel.Id, 2));

        var fila = await w.Mrs.ListAsync(Otavio, queueOnly: true);

        Assert.Single(fila);
        Assert.Equal(MaterialRequisitionStatus.Approved, fila[0].Status);
    }

    // ---- atendimento --------------------------------------------------------
    [Fact]
    public async Task Atendimento_total_encerra_sem_gerar_compra()
    {
        var w = await BuildAsync();
        var mr = await AprovadaAsync(w, new MaterialItemInput(w.Detergente.Id, 10));
        var itemId = mr.Items.Single().Id;

        var (done, error) = await w.Mrs.FulfillAsync(Otavio, mr.Id, [new(itemId, 10)], w.Prs);

        Assert.Null(error);
        Assert.Equal(MaterialRequisitionStatus.Fulfilled, done!.Status);
        Assert.Equal(10m, done.Items.Single().FulfilledQuantity);
        Assert.Null(done.PurchaseRequisitionNumber);
        Assert.Empty(await w.Db.Requisitions.ToListAsync());
    }

    [Fact]
    public async Task Atendimento_parcial_gera_solicitacao_de_compra_do_faltante()
    {
        var w = await BuildAsync();
        var mr = await AprovadaAsync(w,
            new MaterialItemInput(w.Detergente.Id, 10), new MaterialItemInput(w.Papel.Id, 5));
        var detergente = mr.Items.Single(i => i.CatalogItemId == w.Detergente.Id);
        var papel = mr.Items.Single(i => i.CatalogItemId == w.Papel.Id);

        var (done, error) = await w.Mrs.FulfillAsync(Otavio, mr.Id,
            [new(detergente.Id, 6), new(papel.Id, 0)], w.Prs);

        Assert.Null(error);
        Assert.Equal(MaterialRequisitionStatus.PartiallyFulfilled, done!.Status);
        Assert.Equal(MaterialItemStatus.PartiallyFulfilled, done.Items.Single(i => i.Id == detergente.Id).Status);
        Assert.Equal(MaterialItemStatus.PurchaseRoute, done.Items.Single(i => i.Id == papel.Id).Status);

        // a compra do faltante nasce no nome de quem pediu, no mesmo centro de custo
        var pr = await w.Db.Requisitions.Include(r => r.Items).SingleAsync();
        Assert.Equal(done.PurchaseRequisitionNumber, pr.Number);
        Assert.Equal(Ana.Id, pr.RequesterId);
        Assert.Equal("CC-01", pr.CostCenter);
        Assert.Equal(RequisitionStatus.Submitted, pr.Status);
        Assert.Equal(4m, pr.Items.Single(i => i.CatalogItemId == w.Detergente.Id).Quantity);   // 10 − 6
        Assert.Equal(5m, pr.Items.Single(i => i.CatalogItemId == w.Papel.Id).Quantity);
    }

    [Fact]
    public async Task Sem_nada_em_estoque_a_solicitacao_inteira_vira_compra()
    {
        var w = await BuildAsync();
        var mr = await AprovadaAsync(w, new MaterialItemInput(w.Papel.Id, 5));
        var itemId = mr.Items.Single().Id;

        var (done, error) = await w.Mrs.FulfillAsync(Otavio, mr.Id, [new(itemId, 0)], w.Prs);

        Assert.Null(error);
        Assert.Equal(MaterialRequisitionStatus.PurchaseRoute, done!.Status);
        Assert.NotNull(done.PurchaseRequisitionNumber);
        var pr = await w.Db.Requisitions.Include(r => r.Items).SingleAsync();
        Assert.Equal(5m, pr.Items.Single().Quantity);
    }

    [Fact]
    public async Task Entregar_mais_do_que_o_aprovado_e_recusado()
    {
        var w = await BuildAsync();
        var (mr, _) = await w.Mrs.CreateAsync(Ana, "CC-01", null, [new MaterialItemInput(w.Detergente.Id, 10)]);
        var itemId = mr!.Items.Single().Id;
        await w.Mrs.ApproveAsync(Bruno, mr.Id, [new(itemId, 6)], null);

        var (_, error) = await w.Mrs.FulfillAsync(Otavio, mr.Id, [new(itemId, 8)], w.Prs);

        Assert.Equal("MR-ERR-032", error!.Code);
    }

    [Fact]
    public async Task Atender_antes_da_aprovacao_ou_duas_vezes_e_recusado()
    {
        var w = await BuildAsync();
        var (semAprovar, _) = await w.Mrs.CreateAsync(Ana, "CC-01", null, [new MaterialItemInput(w.Detergente.Id, 1)]);
        var (_, cedo) = await w.Mrs.FulfillAsync(Otavio, semAprovar!.Id,
            [new(semAprovar.Items.Single().Id, 1)], w.Prs);
        Assert.Equal("MR-ERR-040", cedo!.Code);

        var mr = await AprovadaAsync(w, new MaterialItemInput(w.Detergente.Id, 2));
        var itemId = mr.Items.Single().Id;
        await w.Mrs.FulfillAsync(Otavio, mr.Id, [new(itemId, 2)], w.Prs);
        var (_, deNovo) = await w.Mrs.FulfillAsync(Otavio, mr.Id, [new(itemId, 1)], w.Prs);
        Assert.Equal("MR-ERR-040", deNovo!.Code);
    }

    // ---- cancelamento e visibilidade ----------------------------------------
    [Fact]
    public async Task Cancelamento_exige_motivo_e_vale_ate_o_atendimento()
    {
        var w = await BuildAsync();
        var (mr, _) = await w.Mrs.CreateAsync(Ana, "CC-01", null, [new MaterialItemInput(w.Detergente.Id, 1)]);

        var (_, semMotivo) = await w.Mrs.CancelAsync(Ana, mr!.Id, " ");
        Assert.Equal("MR-ERR-030", semMotivo!.Code);

        var (cancelled, ok) = await w.Mrs.CancelAsync(Ana, mr.Id, "Pedido em duplicidade");
        Assert.Null(ok);
        Assert.Equal(MaterialRequisitionStatus.Cancelled, cancelled!.Status);

        var (_, jaCancelada) = await w.Mrs.CancelAsync(Ana, mr.Id, "De novo");
        Assert.Equal("MR-ERR-040", jaCancelada!.Code);
    }

    [Fact]
    public async Task Solicitante_ve_apenas_as_proprias()
    {
        var w = await BuildAsync();
        var outra = new Actor(Guid.NewGuid(), "Beto Solicitante", Roles.Requester);
        await w.Mrs.CreateAsync(Ana, "CC-01", null, [new MaterialItemInput(w.Detergente.Id, 1)]);
        await w.Mrs.CreateAsync(outra, "CC-02", null, [new MaterialItemInput(w.Papel.Id, 2)]);

        var daAna = await w.Mrs.ListAsync(Ana, queueOnly: false);

        Assert.Single(daAna);
    }

    // ---- atendimento parcial: as duas decisões de quem atende -----------------
    //
    // Antes, todo atendimento encerrava a solicitação e comprava o faltante. O almoxarifado
    // perdia o caso mais comum: entregou 3 das 10 porque o resto chega na quinta — ou se
    // esperava a carga sem registrar o que já saiu, ou se comprava o que já estava a caminho.

    [Fact]
    public async Task Entrega_parcial_pendente_mantem_a_solicitacao_na_fila_do_estoque()
    {
        var w = await BuildAsync();
        var mr = await AprovadaAsync(w, new MaterialItemInput(w.Detergente.Id, 10));
        var item = mr.Items.Single();

        var (depois, erro) = await w.Mrs.FulfillAsync(Otavio, mr.Id, [new(item.Id, 3)], w.Prs,
            new DecisaoDoAtendimento(Concluir: false, GerarCompra: false));

        Assert.Null(erro);
        Assert.Equal(MaterialRequisitionStatus.Approved, depois!.Status);   // continua na fila
        Assert.Null(depois.FulfilledAt);                                     // não acabou
        Assert.Equal(3, depois.Items.Single().FulfilledQuantity);
        Assert.Null(depois.PurchaseRequisitionNumber);                       // não comprou
        Assert.Equal(MaterialItemStatus.PartiallyFulfilled, depois.Items.Single().Status);
    }

    [Fact]
    public async Task A_segunda_entrega_soma_a_primeira_e_o_teto_e_o_que_ainda_falta()
    {
        // substituir em vez de somar perderia as três primeiras botas no segundo atendimento
        var w = await BuildAsync();
        var mr = await AprovadaAsync(w, new MaterialItemInput(w.Detergente.Id, 10));
        var item = mr.Items.Single();
        await w.Mrs.FulfillAsync(Otavio, mr.Id, [new(item.Id, 3)], w.Prs, new(false, false));

        // entregar 8 agora passaria do aprovado: faltam 7
        var (_, excesso) = await w.Mrs.FulfillAsync(Otavio, mr.Id, [new(item.Id, 8)], w.Prs, new(false, false));
        Assert.Equal("MR-ERR-032", excesso!.Code);
        Assert.Contains("ainda falta 7", excesso.Message);

        var (fim, erro) = await w.Mrs.FulfillAsync(Otavio, mr.Id, [new(item.Id, 7)], w.Prs, new(true, false));
        Assert.Null(erro);
        Assert.Equal(10, fim!.Items.Single().FulfilledQuantity);
        Assert.Equal(MaterialRequisitionStatus.Fulfilled, fim.Status);
    }

    [Fact]
    public async Task Concluir_sem_comprar_encerra_a_solicitacao_curta()
    {
        // "o que faltou não vai ser comprado" é decisão legítima, e antes não existia
        var w = await BuildAsync();
        var mr = await AprovadaAsync(w, new MaterialItemInput(w.Detergente.Id, 10));

        var (depois, erro) = await w.Mrs.FulfillAsync(Otavio, mr.Id, [new(mr.Items.Single().Id, 4)], w.Prs,
            new(Concluir: true, GerarCompra: false));

        Assert.Null(erro);
        Assert.Equal(MaterialRequisitionStatus.PartiallyFulfilled, depois!.Status);
        Assert.NotNull(depois.FulfilledAt);
        Assert.Null(depois.PurchaseRequisitionNumber);
    }

    [Fact]
    public async Task Pendente_sem_entregar_nada_nao_registra_atendimento_nenhum()
    {
        var w = await BuildAsync();
        var mr = await AprovadaAsync(w, new MaterialItemInput(w.Detergente.Id, 10));

        var (_, erro) = await w.Mrs.FulfillAsync(Otavio, mr.Id, [new(mr.Items.Single().Id, 0)], w.Prs,
            new(Concluir: false, GerarCompra: false));

        Assert.Equal("MR-ERR-033", erro!.Code);
    }

    [Fact]
    public async Task A_compra_do_faltante_sai_uma_vez_so_por_solicitacao()
    {
        // a segunda compraria de novo o mesmo faltante que a primeira já pediu
        var w = await BuildAsync();
        var mr = await AprovadaAsync(w, new MaterialItemInput(w.Detergente.Id, 10));
        var item = mr.Items.Single();

        var (primeira, _) = await w.Mrs.FulfillAsync(Otavio, mr.Id, [new(item.Id, 2)], w.Prs, new(false, true));
        var numero = primeira!.PurchaseRequisitionNumber;
        Assert.NotNull(numero);

        var (segunda, _) = await w.Mrs.FulfillAsync(Otavio, mr.Id, [new(item.Id, 2)], w.Prs, new(true, true));

        Assert.Equal(numero, segunda!.PurchaseRequisitionNumber);
        Assert.Equal(1, await w.Db.Requisitions.CountAsync());
    }

    [Fact]
    public async Task O_padrao_continua_sendo_o_de_antes_encerra_e_compra()
    {
        // toda chamada anterior à regra significa isso; mudar o sentido delas em silêncio
        // seria pior que pedir a decisão
        var w = await BuildAsync();
        var mr = await AprovadaAsync(w, new MaterialItemInput(w.Detergente.Id, 10));

        var (depois, erro) = await w.Mrs.FulfillAsync(Otavio, mr.Id, [new(mr.Items.Single().Id, 4)], w.Prs);

        Assert.Null(erro);
        Assert.Equal(MaterialRequisitionStatus.PartiallyFulfilled, depois!.Status);
        Assert.NotNull(depois.FulfilledAt);
        Assert.NotNull(depois.PurchaseRequisitionNumber);
    }
}
