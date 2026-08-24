using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Inventory;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

public class InventoryServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly Actor Almox = new(Guid.NewGuid(), "Otávio Almoxarife", Roles.WarehouseOperator);

    private static (InventoryService svc, CatalogService catalog, AppDbContext db) Build()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        return (new InventoryService(db, clock), new CatalogService(db, clock), db);
    }

    private static async Task<(CatalogItem item, StorageLocation location)> SeedAsync(
        InventoryService svc, CatalogService catalog)
    {
        var (item, e1) = await catalog.CreateAsync(Almox.Id, "LMP-001", "Detergente neutro 500ml", "MATERIAL DE LIMPEZA", "UN", 3.5m);
        Assert.Null(e1);
        var (location, e2) = await svc.CreateLocationAsync(Almox.Id, "ALM-01", "Almoxarifado Central");
        Assert.Null(e2);
        return (item!, location!);
    }

    [Fact]
    public async Task Entrada_cria_saldo_e_movimento_com_antes_e_depois()
    {
        var (svc, catalog, db) = Build();
        var (item, location) = await SeedAsync(svc, catalog);

        var (mov, error) = await svc.RegisterEntryAsync(Almox, item.Id, location.Id, 50, MovementOrigin.Receiving, "NF-1234");

        Assert.Null(error);
        Assert.Equal(MovementType.Entry, mov!.Type);
        Assert.Equal(0, mov.BalanceBefore);
        Assert.Equal(50, mov.BalanceAfter);
        Assert.StartsWith("MOV-2026-", mov.Number);
        var balance = await db.StockBalances.SingleAsync();
        Assert.Equal(50, balance.TotalQty);
        Assert.Equal(50, balance.AvailableQty);
    }

    [Fact]
    public async Task Saida_baixa_o_saldo_e_registra_movimento_vinculado()
    {
        var (svc, catalog, db) = Build();
        var (item, location) = await SeedAsync(svc, catalog);
        await svc.RegisterEntryAsync(Almox, item.Id, location.Id, 50, MovementOrigin.Receiving, "NF-1234");

        var (mov, error) = await svc.RegisterIssueAsync(Almox, item.Id, location.Id, 20, MovementOrigin.Consumption, "REQ-01");

        Assert.Null(error);
        Assert.Equal(50, mov!.BalanceBefore);
        Assert.Equal(30, mov.BalanceAfter);
        Assert.Equal(30, (await db.StockBalances.SingleAsync()).TotalQty);
    }

    [Fact]
    public async Task Saida_maior_que_o_disponivel_e_recusada_com_IV_ERR_020()
    {
        var (svc, catalog, _) = Build();
        var (item, location) = await SeedAsync(svc, catalog);
        await svc.RegisterEntryAsync(Almox, item.Id, location.Id, 10, MovementOrigin.Receiving, "NF-1");

        var (_, error) = await svc.RegisterIssueAsync(Almox, item.Id, location.Id, 11, MovementOrigin.Consumption, "REQ-02");

        Assert.Equal("IV-ERR-020", error!.Code);
    }

    [Fact]
    public async Task Quantidade_zero_e_recusada_com_IV_ERR_009()
    {
        var (svc, catalog, _) = Build();
        var (item, location) = await SeedAsync(svc, catalog);

        var (_, error) = await svc.RegisterEntryAsync(Almox, item.Id, location.Id, 0, MovementOrigin.Receiving, "NF-1");

        Assert.Equal("IV-ERR-009", error!.Code);
    }

    [Fact]
    public async Task Entrada_sem_documento_de_origem_e_recusada_com_IV_ERR_002()
    {
        var (svc, catalog, _) = Build();
        var (item, location) = await SeedAsync(svc, catalog);

        var (_, error) = await svc.RegisterEntryAsync(Almox, item.Id, location.Id, 5, MovementOrigin.Receiving, "  ");

        Assert.Equal("IV-ERR-002", error!.Code);
    }

    [Fact]
    public async Task Entrada_de_item_inativo_e_bloqueada_mas_devolucao_e_permitida()
    {
        var (svc, catalog, db) = Build();
        var (item, location) = await SeedAsync(svc, catalog);
        var tracked = await db.CatalogItems.SingleAsync(i => i.Id == item.Id);
        tracked.Active = false;
        await db.SaveChangesAsync();

        var (_, blocked) = await svc.RegisterEntryAsync(Almox, item.Id, location.Id, 5, MovementOrigin.Receiving, "NF-2");
        var (mov, allowed) = await svc.RegisterEntryAsync(Almox, item.Id, location.Id, 5, MovementOrigin.Return, "DEV-01");

        Assert.Equal("IV-ERR-010", blocked!.Code);
        Assert.Null(allowed);
        Assert.Equal(5, mov!.BalanceAfter);
    }

    [Fact]
    public async Task Origem_incompativel_com_o_tipo_e_recusada()
    {
        var (svc, catalog, _) = Build();
        var (item, location) = await SeedAsync(svc, catalog);

        var (_, e1) = await svc.RegisterEntryAsync(Almox, item.Id, location.Id, 5, MovementOrigin.Consumption, "X");
        var (_, e2) = await svc.RegisterIssueAsync(Almox, item.Id, location.Id, 5, MovementOrigin.Receiving, "X");

        Assert.Equal("IV-ERR-010", e1!.Code);
        Assert.Equal("IV-ERR-011", e2!.Code);
    }

    [Fact]
    public async Task Local_inativo_e_recusado_com_IV_ERR_060()
    {
        var (svc, catalog, db) = Build();
        var (item, location) = await SeedAsync(svc, catalog);
        var tracked = await db.StorageLocations.SingleAsync(l => l.Id == location.Id);
        tracked.Active = false;
        await db.SaveChangesAsync();

        var (_, error) = await svc.RegisterEntryAsync(Almox, item.Id, location.Id, 5, MovementOrigin.Receiving, "NF-3");

        Assert.Equal("IV-ERR-060", error!.Code);
    }

    [Fact]
    public void Permissoes_operar_e_do_almoxarifado_e_visualizar_inclui_auditor()
    {
        Assert.True(InventoryService.CanOperate(Roles.WarehouseOperator));
        Assert.True(InventoryService.CanOperate(Roles.WarehouseSupervisor));
        Assert.True(InventoryService.CanOperate(Roles.SupplyManager));
        Assert.False(InventoryService.CanOperate(Roles.Requester));
        Assert.False(InventoryService.CanOperate(Roles.Auditor));
        Assert.True(InventoryService.CanView(Roles.Auditor));
        Assert.False(InventoryService.CanView(Roles.Requester));
    }
}
