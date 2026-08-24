using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// Cadastro de produto: item de almoxarifado (saldo controlado + estoque mínimo),
/// código automático pela família e fornecedores em texto livre.
/// </summary>
public class CatalogStockTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly Guid Actor = Guid.NewGuid();

    private static CatalogService Build(out AppDbContext db)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        db = new AppDbContext(options);
        return new CatalogService(db, new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task Codigo_e_gerado_pela_familia_quando_nao_informado()
    {
        var svc = Build(out _);
        var (a, e1) = await svc.CreateAsync(Actor, null, "Detergente neutro 500ml", "Material de Limpeza", "UN", 2m);
        Assert.Null(e1);
        Assert.Equal("MAT-001", a!.Code);

        var (b, _) = await svc.CreateAsync(Actor, null, "Sabão em pó 1kg", "Material de Limpeza", "UN", 9m);
        Assert.Equal("MAT-002", b!.Code);
    }

    [Fact]
    public async Task Codigo_proprio_informado_e_mantido()
    {
        var svc = Build(out _);
        var (item, error) = await svc.CreateAsync(Actor, "lmp-001", "Detergente neutro", "Material de Limpeza", "UN", 2m);
        Assert.Null(error);
        Assert.Equal("LMP-001", item!.Code);

        var (_, dup) = await svc.CreateAsync(Actor, "LMP-001", "Outro produto", "Material de Limpeza", "UN", 3m);
        Assert.Equal("IC-ERR-010", dup!.Code);
    }

    [Fact]
    public async Task Item_de_almoxarifado_guarda_estoque_minimo_e_aparece_no_filtro()
    {
        var svc = Build(out _);
        await svc.CreateAsync(Actor, null, "Detergente neutro", "Material de Limpeza", "UN", 2m,
            stockControlled: true, minimumQty: 12m);
        await svc.CreateAsync(Actor, null, "Notebook i5", "Equipamentos", "UN", 4500m);   // compra direta

        var estoque = await svc.ListAsync(null, null, false, stockOnly: true);
        var item = Assert.Single(estoque);
        Assert.Equal("Detergente neutro", item.Description);
        Assert.Equal(12m, item.MinimumQty);

        var todos = await svc.ListAsync(null, null, false);
        Assert.Equal(2, todos.Count);
    }

    [Fact]
    public async Task Estoque_minimo_negativo_e_recusado()
    {
        var svc = Build(out _);
        var (_, error) = await svc.CreateAsync(Actor, null, "Item", "Material de Limpeza", "UN", null,
            stockControlled: true, minimumQty: -1m);
        Assert.Equal("IC-ERR-016", error!.Code);
    }

    [Fact]
    public async Task Produto_aceita_varios_fornecedores_sem_cadastro_na_plataforma()
    {
        var svc = Build(out _);
        var (item, error) = await svc.CreateAsync(Actor, null, "Detergente neutro", "Material de Limpeza", "UN", 2m,
            suppliers: [
                new ItemSupplierInput("Distribuidora Alfa", "12.345.678/0001-90", "alfa@fornecedor.com", "DET-500", 1.90m, "Caixa com 12"),
                new ItemSupplierInput("Comercial Beta", null, "(81) 3333-4444", null, 2.10m, null),
            ]);
        Assert.Null(error);
        Assert.Equal(2, item!.Suppliers.Count);
        var alfa = item.Suppliers.Single(s => s.SupplierName == "Distribuidora Alfa");
        Assert.Equal("12345678000190", alfa.TaxId);        // guarda só os dígitos
        Assert.Equal("DET-500", alfa.SupplierItemCode);
        Assert.Null(item.Suppliers.Single(s => s.SupplierName == "Comercial Beta").TaxId);
    }

    // ---- famílias: cadastro próprio evita variações da mesma família ---------

    [Fact]
    public async Task Familia_nao_cadastrada_e_recusada_quando_ja_existe_cadastro()
    {
        var svc = Build(out _);
        await svc.CreateFamilyAsync(Actor, "material de limpeza", null);

        var (_, error) = await svc.CreateAsync(Actor, null, "Detergente", "LIMPEZA", "UN", 2m);
        Assert.Equal("IC-ERR-022", error!.Code);

        var (ok, none) = await svc.CreateAsync(Actor, null, "Detergente", "Material de Limpeza", "UN", 2m);
        Assert.Null(none);
        Assert.Equal("MATERIAL DE LIMPEZA", ok!.Family);
    }

    [Fact]
    public async Task Familia_duplicada_e_recusada_e_o_nome_e_normalizado()
    {
        var svc = Build(out _);
        var (family, error) = await svc.CreateFamilyAsync(Actor, "  material de limpeza  ", "Produtos de higiene");
        Assert.Null(error);
        Assert.Equal("MATERIAL DE LIMPEZA", family!.Name);

        var (_, dup) = await svc.CreateFamilyAsync(Actor, "Material De Limpeza", null);
        Assert.Equal("IC-ERR-021", dup!.Code);
    }

    [Fact]
    public async Task Renomear_familia_leva_os_produtos_junto()
    {
        var svc = Build(out var db);
        var (family, _) = await svc.CreateFamilyAsync(Actor, "LIMPEZA", null);
        var (item, _) = await svc.CreateAsync(Actor, null, "Detergente", "LIMPEZA", "UN", 2m);

        var (renamed, error) = await svc.UpdateFamilyAsync(family!.Id, "Material de Limpeza", null, null);
        Assert.Null(error);
        Assert.Equal("MATERIAL DE LIMPEZA", renamed!.Name);

        var reloaded = await db.CatalogItems.SingleAsync(i => i.Id == item!.Id);
        Assert.Equal("MATERIAL DE LIMPEZA", reloaded.Family);
    }

    [Fact]
    public async Task Renomear_para_familia_existente_unifica_as_duas()
    {
        var svc = Build(out var db);
        var (limpeza, _) = await svc.CreateFamilyAsync(Actor, "LIMPEZA", null);
        await svc.CreateFamilyAsync(Actor, "MATERIAL DE LIMPEZA", null);
        var (item, _) = await svc.CreateAsync(Actor, null, "Detergente", "LIMPEZA", "UN", 2m);

        var (merged, error) = await svc.UpdateFamilyAsync(limpeza!.Id, "Material de Limpeza", null, null);
        Assert.Null(error);
        Assert.Equal("MATERIAL DE LIMPEZA", merged!.Name);

        var families = await svc.ListFamiliesAsync(true);
        Assert.Single(families);                                   // as duas viraram uma
        var reloaded = await db.CatalogItems.SingleAsync(i => i.Id == item!.Id);
        Assert.Equal("MATERIAL DE LIMPEZA", reloaded.Family);      // o produto acompanhou
    }

    [Fact]
    public async Task Base_sem_cadastro_de_familias_continua_aceitando_qualquer_familia()
    {
        var svc = Build(out _);
        var (item, error) = await svc.CreateAsync(Actor, null, "Detergente", "QUALQUER COISA", "UN", 2m);
        Assert.Null(error);
        Assert.Equal("QUALQUER COISA", item!.Family);
    }

    [Fact]
    public async Task Edicao_substitui_a_lista_de_fornecedores_do_produto()
    {
        var svc = Build(out var db);
        var (item, _) = await svc.CreateAsync(Actor, null, "Detergente neutro", "Material de Limpeza", "UN", 2m,
            suppliers: [new ItemSupplierInput("Distribuidora Alfa", null, null, null, 1.9m, null)]);

        var (updated, error) = await svc.UpdateAsync(item!.Id, null, null, null, null, null,
            stockControlled: true, minimumQty: 20m,
            suppliers: [new ItemSupplierInput("Comercial Beta", null, null, null, 2.0m, null)]);
        Assert.Null(error);
        Assert.True(updated!.StockControlled);
        Assert.Equal(20m, updated.MinimumQty);
        var only = Assert.Single(updated.Suppliers);
        Assert.Equal("Comercial Beta", only.SupplierName);
        Assert.Equal(1, await db.CatalogItemSuppliers.CountAsync());   // o antigo saiu junto
    }
}
