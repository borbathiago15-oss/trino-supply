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
        var mrs = new MaterialRequisitionService(db, catalog, inv, clock);
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
}
