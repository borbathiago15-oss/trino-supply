using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Analytics;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Inventory;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Tests;

public class AnalyticsServiceTests
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
    private static readonly Actor Otavio = new(Guid.NewGuid(), "Otávio Almoxarife", Roles.WarehouseOperator);

    private static object Prop(object obj, string name) =>
        obj.GetType().GetProperty(name)!.GetValue(obj)!;

    private sealed record World(
        AnalyticsService Analytics, CostCenterService Ccs, RequisitionService Prs,
        PurchaseOrderService Pos, InventoryService Inv, CatalogService Catalog,
        SupplierService Sup, AppDbContext Db, FixedTimeProvider Clock);

    private static World Build()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        var catalog = new CatalogService(db, clock);
        var inv = new InventoryService(db, clock);
        return new World(
            new AnalyticsService(db, clock), new CostCenterService(db, clock),
            new RequisitionService(db, new FakeNumbers(), catalog, clock),
            new PurchaseOrderService(db, inv, clock), inv, catalog,
            new SupplierService(db, clock), db, clock);
    }

    [Fact]
    public async Task Centro_de_custo_codigo_unico_e_dimensoes_normalizadas()
    {
        var w = Build();

        var (cc, ok) = await w.Ccs.CreateAsync(Ana.Id, "cc-ne-01", "Filial Recife", "nordeste", "Marina Lima", "Cliente Alfa");
        Assert.Null(ok);
        Assert.Equal("CC-NE-01", cc!.Code);
        Assert.Equal("NORDESTE", cc.Region);

        var (_, dup) = await w.Ccs.CreateAsync(Ana.Id, "CC-NE-01", "Outra", null, null, null);
        Assert.Equal("CC-ERR-010", dup!.Code);
    }

    [Fact]
    public async Task Dashboard_de_suprimentos_agrega_por_regional_gerente_e_cliente()
    {
        var w = Build();
        await w.Ccs.CreateAsync(Ana.Id, "CC-NE-01", "Filial Recife", "NORDESTE", "Marina Lima", "Cliente Alfa");
        await w.Ccs.CreateAsync(Ana.Id, "CC-SP-01", "Matriz SP", "SUDESTE", "Paulo Souza", "Cliente Beta");

        var (pr1, _) = await w.Prs.CreateAsync(Ana, "Limpeza NE", "CC-NE-01", "NORMAL", null,
            [new ItemInput("Detergente", 10, "UN", 5, null)]);
        await w.Prs.SubmitAsync(Ana, pr1!.Id);
        await w.Prs.ApproveAsync(Bruno, pr1.Id, null);
        var (pr2, _) = await w.Prs.CreateAsync(Ana, "Escritório SP", "CC-SP-01", "NORMAL", null,
            [new ItemInput("Papel", 4, "PC", 25, null)]);
        await w.Prs.SubmitAsync(Ana, pr2!.Id);

        var dash = await w.Analytics.SupplyAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
            null, null, null, null, null, null, null, null);
        var kpis = Prop(dash, "kpis");
        Assert.Equal(2, (int)Prop(kpis, "prCount"));
        Assert.Equal(1, (int)Prop(kpis, "approvedCount"));
        Assert.Equal(1, (int)Prop(kpis, "pendingApproval"));
        var regions = (List<object>)Prop(Prop(dash, "rankings"), "regions");
        Assert.Equal(2, regions.Count);
        Assert.Contains(regions, r => (string)Prop(r, "label") == "NORDESTE" && (decimal)Prop(r, "value") == 50m);

        // filtro por regional restringe tudo
        var ne = await w.Analytics.SupplyAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
            null, null, null, null, null, "NORDESTE", null, null);
        Assert.Equal(1, (int)Prop(Prop(ne, "kpis"), "prCount"));

        var gerente = await w.Analytics.SupplyAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
            null, null, null, null, null, null, "Paulo Souza", null);
        Assert.Equal(1, (int)Prop(Prop(gerente, "kpis"), "prCount"));

        var cliente = await w.Analytics.SupplyAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
            null, null, null, null, null, null, null, "Cliente Alfa");
        Assert.Equal(1, (int)Prop(Prop(cliente, "kpis"), "prCount"));
    }

    [Fact]
    public async Task Dashboard_de_suprimentos_filtra_pedidos_por_fornecedor_e_comprador()
    {
        var w = Build();
        var (sup1, _) = await w.Sup.CreateAsync(Carla.Id, "Alfa LTDA", "Alfa", "12345678000190", null, null);
        var (sup2, _) = await w.Sup.CreateAsync(Carla.Id, "Beta LTDA", "Beta", "98765432000110", null, null);
        await w.Pos.CreateAsync(Carla, sup1!.Id, null, [new PoItemInput("Item A", 2, "UN", 100, null)], null);
        await w.Pos.CreateAsync(Carla, sup2!.Id, null, [new PoItemInput("Item B", 1, "UN", 40, null)], null);

        var all = await w.Analytics.SupplyAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
            null, null, null, null, null, null, null, null);
        Assert.Equal(240m, (decimal)Prop(Prop(all, "kpis"), "poTotalValue"));

        var onlyAlfa = await w.Analytics.SupplyAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
            sup1.Id, null, null, null, null, null, null, null);
        Assert.Equal(200m, (decimal)Prop(Prop(onlyAlfa, "kpis"), "poTotalValue"));
        Assert.Equal(1, (int)Prop(Prop(onlyAlfa, "kpis"), "poCount"));
    }

    [Fact]
    public async Task Dashboard_de_estoque_valoriza_saldo_e_lista_rupturas()
    {
        var w = Build();
        var (det, _) = await w.Catalog.CreateAsync(Otavio.Id, "LMP-001", "Detergente", "MATERIAL DE LIMPEZA", "UN", 4m);
        var (papel, _) = await w.Catalog.CreateAsync(Otavio.Id, "ESC-001", "Papel A4", "MATERIAL DE ESCRITORIO", "PC", 25m);
        var (loc, _) = await w.Inv.CreateLocationAsync(Otavio.Id, "ALM-01", "Central");
        await w.Inv.RegisterEntryAsync(Otavio, det!.Id, loc!.Id, 50, MovementOrigin.Receiving, "NF 1");
        await w.Inv.RegisterIssueAsync(Otavio, det.Id, loc.Id, 10, MovementOrigin.Consumption, "OS 1");

        var dash = await w.Analytics.StockAsync(null, null, 6);
        var kpis = Prop(dash, "kpis");
        Assert.Equal(1, (int)Prop(kpis, "skusWithBalance"));
        Assert.Equal(160m, (decimal)Prop(kpis, "totalValue"));       // 40 × R$4
        Assert.Equal(50m, (decimal)Prop(kpis, "entriesQty"));
        Assert.Equal(10m, (decimal)Prop(kpis, "issuesQty"));
        Assert.Equal(1, (int)Prop(kpis, "stockoutCount"));           // papel sem saldo

        var stockout = ((System.Collections.IEnumerable)Prop(dash, "stockout")).Cast<object>().ToList();
        Assert.Contains(stockout, s => (string)Prop(s, "code") == "ESC-001");

        // filtro por família limpa a ruptura do papel
        var soLimpeza = await w.Analytics.StockAsync(null, "MATERIAL DE LIMPEZA", 6);
        Assert.Equal(0, (int)Prop(Prop(soLimpeza, "kpis"), "stockoutCount"));
    }
}
