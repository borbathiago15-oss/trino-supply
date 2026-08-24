using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

public class CatalogServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeNumbers : IPrNumberGenerator
    {
        private int _next;
        public Task<string> NextAsync(CancellationToken ct = default) => Task.FromResult($"PR-2026-{++_next:000000}");
    }

    private static readonly Actor Gestor = new(Guid.NewGuid(), "Gestor", Roles.SupplyManager);
    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana", Roles.Requester);

    private static (CatalogService catalog, RequisitionService reqs, AppDbContext db) Build()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        var catalog = new CatalogService(db, clock);
        return (catalog, new RequisitionService(db, new FakeNumbers(), catalog, clock), db);
    }

    [Fact]
    public async Task Criacao_normaliza_codigo_e_familia_e_bloqueia_duplicado()
    {
        var (catalog, _, _) = Build();

        var (item, ok) = await catalog.CreateAsync(Gestor.Id, "lmp-001", "Detergente neutro 500ml", "material de limpeza", "un", 3.50m);
        var (_, dup) = await catalog.CreateAsync(Gestor.Id, "LMP-001", "Outro", "MATERIAL DE LIMPEZA", null, null);

        Assert.Null(ok);
        Assert.Equal("LMP-001", item!.Code);
        Assert.Equal("MATERIAL DE LIMPEZA", item.Family);
        Assert.Equal("IC-ERR-010", dup!.Code);
    }

    [Fact]
    public async Task Listagem_por_familia_retorna_somente_ativos_para_solicitantes()
    {
        var (catalog, _, _) = Build();
        var (detergente, _) = await catalog.CreateAsync(Gestor.Id, "LMP-001", "Detergente neutro", "MATERIAL DE LIMPEZA", "UN", 3.50m);
        await catalog.CreateAsync(Gestor.Id, "LMP-002", "Água sanitária 1L", "MATERIAL DE LIMPEZA", "UN", 5.90m);
        await catalog.CreateAsync(Gestor.Id, "ESC-001", "Papel A4 (resma)", "MATERIAL DE ESCRITORIO", "RES", 28.00m);
        await catalog.UpdateAsync(detergente!.Id, null, null, null, null, active: false);

        var limpeza = await catalog.ListAsync("MATERIAL DE LIMPEZA", null, includeInactive: false);
        var familias = await catalog.FamiliesAsync(onlyActive: true);

        Assert.Single(limpeza);
        Assert.Equal("LMP-002", limpeza[0].Code);
        Assert.Equal(["MATERIAL DE ESCRITORIO", "MATERIAL DE LIMPEZA"], familias);
    }

    [Fact]
    public async Task Requisicao_por_catalogo_usa_snapshot_de_descricao_unidade_e_preco()
    {
        var (catalog, reqs, _) = Build();
        var (papel, _) = await catalog.CreateAsync(Gestor.Id, "ESC-001", "Papel A4 (resma)", "MATERIAL DE ESCRITORIO", "RES", 28.00m);
        var (caneta, _) = await catalog.CreateAsync(Gestor.Id, "ESC-002", "Caneta esferográfica azul", "MATERIAL DE ESCRITORIO", "UN", 2.20m);

        var (pr, error) = await reqs.CreateAsync(Ana, "Reposição do escritório", "CC-ADM-001", null, null,
        [
            new ItemInput("", 10, null, null, null, papel!.Id),
            new ItemInput("", 50, null, null, null, caneta!.Id),
        ], "CATALOGO");

        Assert.Null(error);
        Assert.Equal("CATALOGO", pr!.Kind);
        var papelItem = pr.Items.Single(i => i.CatalogCode == "ESC-001");
        Assert.Equal("Papel A4 (resma)", papelItem.Description);
        Assert.Equal("RES", papelItem.UnitOfMeasure);
        Assert.Equal(28.00m, papelItem.EstimatedUnitPrice);
        Assert.Equal(10 * 28.00m + 50 * 2.20m, pr.TotalEstimatedValue);
    }

    [Fact]
    public async Task Item_inativo_nao_pode_ser_solicitado()
    {
        var (catalog, reqs, _) = Build();
        var (item, _) = await catalog.CreateAsync(Gestor.Id, "LMP-001", "Detergente neutro", "MATERIAL DE LIMPEZA", "UN", 3.50m);
        await catalog.UpdateAsync(item!.Id, null, null, null, null, active: false);

        var (_, error) = await reqs.CreateAsync(Ana, "Limpeza", "CC-ADM-001", null, null,
            [new ItemInput("", 5, null, null, null, item.Id)], "CATALOGO");

        Assert.Equal("PR-ERR-022", error!.Code);
    }

    [Fact]
    public async Task Requisicao_por_catalogo_recusa_item_avulso()
    {
        var (_, reqs, _) = Build();

        var (_, error) = await reqs.CreateAsync(Ana, "Mista inválida", "CC-ADM-001", null, null,
            [new ItemInput("Item digitado à mão", 1, "UN", 10, null)], "CATALOGO");

        Assert.Equal("PR-ERR-030", error!.Code);
    }

    [Fact]
    public async Task Requisicao_avulsa_continua_funcionando_como_antes()
    {
        var (_, reqs, _) = Build();

        var (pr, error) = await reqs.CreateAsync(Ana, "Compra pontual", "CC-ADM-001", null, null,
            [new ItemInput("Serviço de calibração de balança", 1, "SV", 850, null)]);

        Assert.Null(error);
        Assert.Equal("AVULSA", pr!.Kind);
        Assert.Null(pr.Items.Single().CatalogItemId);
    }
}
