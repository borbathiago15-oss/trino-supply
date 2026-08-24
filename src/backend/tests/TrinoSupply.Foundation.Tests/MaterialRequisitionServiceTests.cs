using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Inventory;
using TrinoSupply.Foundation.Api.Materials;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

public class MaterialRequisitionServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana Solicitante", Roles.Requester);
    private static readonly Actor Otavio = new(Guid.NewGuid(), "Otávio Almoxarife", Roles.WarehouseOperator);

    private sealed record World(
        MaterialRequisitionService Mrs, InventoryService Inv, CatalogService Catalog, AppDbContext Db,
        CatalogItem Detergente, CatalogItem Papel, StorageLocation Local);

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

        var (detergente, _) = await catalog.CreateAsync(Otavio.Id, "LMP-001", "Detergente neutro 500ml", "MATERIAL DE LIMPEZA", "UN", 3.5m);
        var (papel, _) = await catalog.CreateAsync(Otavio.Id, "ESC-001", "Papel A4 resma 500fl", "MATERIAL DE ESCRITORIO", "PC", 25m);
        var (local, _) = await inv.CreateLocationAsync(Otavio.Id, "ALM-01", "Almoxarifado Central");
        return new World(mrs, inv, catalog, db, detergente!, papel!, local!);
    }

    [Fact]
    public async Task Criacao_exige_itens_do_catalogo_e_gera_numero_MR()
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

    [Fact]
    public async Task Atendimento_total_baixa_estoque_e_vincula_movimentos()
    {
        var w = await BuildAsync();
        await w.Inv.RegisterEntryAsync(Otavio, w.Detergente.Id, w.Local.Id, 50, MovementOrigin.Receiving, "NF-1");
        var (mr, _) = await w.Mrs.CreateAsync(Ana, "CC-01", null, [new MaterialItemInput(w.Detergente.Id, 10)]);

        var (done, error) = await w.Mrs.FulfillAsync(Otavio, mr!.Id, w.Local.Id);

        Assert.Null(error);
        Assert.Equal(MaterialRequisitionStatus.Fulfilled, done!.Status);
        var item = done.Items.Single();
        Assert.Equal(MaterialItemStatus.Fulfilled, item.Status);
        Assert.NotNull(item.StockMovementId);
        var mov = await w.Db.StockMovements.SingleAsync(m => m.Id == item.StockMovementId);
        Assert.Equal(MovementOrigin.Fulfillment, mov.Origin);
        Assert.Equal(mr.Id, mov.MaterialRequisitionId);
        Assert.Equal(40, (await w.Db.StockBalances.SingleAsync()).TotalQty);
    }

    [Fact]
    public async Task Atendimento_parcial_manda_item_sem_saldo_para_rota_de_compra()
    {
        var w = await BuildAsync();
        await w.Inv.RegisterEntryAsync(Otavio, w.Detergente.Id, w.Local.Id, 50, MovementOrigin.Receiving, "NF-1");
        var (mr, _) = await w.Mrs.CreateAsync(Ana, "CC-01", null,
            [new MaterialItemInput(w.Detergente.Id, 10), new MaterialItemInput(w.Papel.Id, 5)]);

        var (done, error) = await w.Mrs.FulfillAsync(Otavio, mr!.Id, w.Local.Id);

        Assert.Null(error);
        Assert.Equal(MaterialRequisitionStatus.PartiallyFulfilled, done!.Status);
        Assert.Equal(MaterialItemStatus.Fulfilled, done.Items.Single(i => i.CatalogItemId == w.Detergente.Id).Status);
        Assert.Equal(MaterialItemStatus.PurchaseRoute, done.Items.Single(i => i.CatalogItemId == w.Papel.Id).Status);
    }

    [Fact]
    public async Task Sem_saldo_algum_tudo_vai_para_rota_de_compra_sem_movimentar_estoque()
    {
        var w = await BuildAsync();
        var (mr, _) = await w.Mrs.CreateAsync(Ana, "CC-01", null, [new MaterialItemInput(w.Papel.Id, 5)]);

        var (done, error) = await w.Mrs.FulfillAsync(Otavio, mr!.Id, w.Local.Id);

        Assert.Null(error);
        Assert.Equal(MaterialRequisitionStatus.PurchaseRoute, done!.Status);
        Assert.Empty(await w.Db.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task Atender_duas_vezes_e_recusado_com_MR_ERR_040()
    {
        var w = await BuildAsync();
        await w.Inv.RegisterEntryAsync(Otavio, w.Detergente.Id, w.Local.Id, 50, MovementOrigin.Receiving, "NF-1");
        var (mr, _) = await w.Mrs.CreateAsync(Ana, "CC-01", null, [new MaterialItemInput(w.Detergente.Id, 10)]);
        await w.Mrs.FulfillAsync(Otavio, mr!.Id, w.Local.Id);

        var (_, error) = await w.Mrs.FulfillAsync(Otavio, mr.Id, w.Local.Id);

        Assert.Equal("MR-ERR-040", error!.Code);
    }

    [Fact]
    public async Task Cancelamento_exige_motivo_e_so_enquanto_aguarda_atendimento()
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
    public async Task Solicitante_ve_apenas_as_proprias_e_almoxarifado_ve_a_fila()
    {
        var w = await BuildAsync();
        var outra = new Actor(Guid.NewGuid(), "Beto Solicitante", Roles.Requester);
        await w.Mrs.CreateAsync(Ana, "CC-01", null, [new MaterialItemInput(w.Detergente.Id, 1)]);
        await w.Mrs.CreateAsync(outra, "CC-02", null, [new MaterialItemInput(w.Papel.Id, 2)]);

        var daAna = await w.Mrs.ListAsync(Ana, queueOnly: false);
        var fila = await w.Mrs.ListAsync(Otavio, queueOnly: true);

        Assert.Single(daAna);
        Assert.Equal(2, fila.Count);
    }
}
