using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Inventory;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// Excluir no cadastro (só o que nunca circulou) e a empresa da SC vinda do cadastro de CNPJs.
/// </summary>
public class CatalogoESolicitacaoTests
{
    private sealed class Relogio : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class Numeros : IPrNumberGenerator
    {
        private int _n;
        public Task<string> NextAsync(CancellationToken ct = default) => Task.FromResult($"PR-2026-{++_n:000000}");
    }

    private static AppDbContext Banco() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<CatalogItem> ProdutoAsync(AppDbContext db, string codigo = "LMP-001", string familia = "LIMPEZA",
        bool ativo = true)
    {
        var item = new CatalogItem { Code = codigo, Description = "Detergente neutro 5L", Family = familia, Active = ativo };
        item.Suppliers.Add(new CatalogItemSupplier { CatalogItemId = item.Id, SupplierName = "Alfa" });
        db.CatalogItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    // ------------------------------------------------------------------ produto

    [Fact]
    public async Task Produto_que_nunca_circulou_sai_de_vez_com_a_foto_e_os_fornecedores()
    {
        var db = Banco();
        var item = await ProdutoAsync(db);
        db.StoredDocuments.Add(new StoredDocument { EntityType = CatalogService.TipoDaFoto, EntityId = item.Id, FileName = "f.png" });
        db.StockBalances.Add(new StockBalance { CatalogItemId = item.Id });   // linha zerada, sem movimento
        await db.SaveChangesAsync();

        var erro = await new CatalogService(db, new Relogio()).ExcluirAsync(item.Id);

        Assert.Null(erro);
        Assert.Empty(await db.CatalogItems.ToListAsync());
        Assert.Empty(await db.CatalogItemSuppliers.ToListAsync());
        Assert.Empty(await db.StoredDocuments.ToListAsync());
        Assert.Empty(await db.StockBalances.ToListAsync());
    }

    [Fact]
    public async Task Produto_que_ja_circulou_nao_se_exclui_e_a_mensagem_diz_onde()
    {
        var db = Banco();
        var item = await ProdutoAsync(db);
        db.RequisitionItems.Add(new RequisitionItem { CatalogItemId = item.Id, Description = "Detergente", UnitOfMeasure = "UN", Quantity = 1 });
        db.RequisitionItems.Add(new RequisitionItem { CatalogItemId = item.Id, Description = "Detergente", UnitOfMeasure = "UN", Quantity = 2 });
        await db.SaveChangesAsync();

        var erro = await new CatalogService(db, new Relogio()).ExcluirAsync(item.Id);

        Assert.Equal("IC-ERR-030", erro!.Code);
        Assert.Contains("2 itens de solicitação de compra", erro.Message);
        Assert.Contains("Inative", erro.Message);
        Assert.Single(await db.CatalogItems.ToListAsync());
    }

    [Fact]
    public async Task Saldo_em_estoque_segura_o_produto()
    {
        var db = Banco();
        var item = await ProdutoAsync(db);
        db.StockBalances.Add(new StockBalance { CatalogItemId = item.Id, TotalQty = 3 });
        await db.SaveChangesAsync();

        var erro = await new CatalogService(db, new Relogio()).ExcluirAsync(item.Id);
        Assert.Equal("IC-ERR-030", erro!.Code);
        Assert.Contains("saldo em estoque", erro.Message);
    }

    [Fact]
    public async Task Produto_inexistente_responde_404()
    {
        var erro = await new CatalogService(Banco(), new Relogio()).ExcluirAsync(Guid.NewGuid());
        Assert.Equal("IC-ERR-404", erro!.Code);
    }

    [Fact]
    public async Task A_ficha_traz_os_fornecedores()
    {
        var db = Banco();
        var item = await ProdutoAsync(db);
        var ficha = await new CatalogService(db, new Relogio()).DetalheAsync(item.Id);
        Assert.Equal("Alfa", Assert.Single(ficha!.Suppliers).SupplierName);
    }

    // ------------------------------------------------------------------ família

    [Fact]
    public async Task Familia_vazia_se_exclui()
    {
        var db = Banco();
        var familia = new ProductFamily { Name = "SERVIÇOS GRÁFICOS" };
        db.ProductFamilies.Add(familia);
        await db.SaveChangesAsync();

        Assert.Null(await new CatalogService(db, new Relogio()).ExcluirFamiliaAsync(familia.Id));
        Assert.Empty(await db.ProductFamilies.ToListAsync());
    }

    [Fact]
    public async Task Familia_com_produto_inativo_tambem_nao_se_exclui()
    {
        // a contagem da tela é dos ativos; a da exclusão conta todos, senão o inativo ficaria órfão
        var db = Banco();
        var familia = new ProductFamily { Name = "MATERIAL DE LIMPEZA", Active = false };
        db.ProductFamilies.Add(familia);
        await ProdutoAsync(db, familia: "MATERIAL DE LIMPEZA", ativo: false);

        var erro = await new CatalogService(db, new Relogio()).ExcluirFamiliaAsync(familia.Id);
        Assert.Equal("IC-ERR-031", erro!.Code);
        Assert.Contains("1 produto(s)", erro.Message);
        Assert.Single(await db.ProductFamilies.ToListAsync());
    }

    // ------------------------------------------------------------------ empresa da SC

    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana", Roles.Requester);
    private static readonly ItemInput Item = new("Resma de papel A4", 10, "UN", null, null);

    private static RequisitionService Servico(AppDbContext db) =>
        new(db, new Numeros(), new CatalogService(db, new Relogio()), new Relogio());

    private static RequisitionService.ScHeaderInput Cabecalho(string? empresa) => new(null, null, empresa, null);

    private static async Task<AppDbContext> ComEmpresasAsync()
    {
        var db = Banco();
        db.Companies.AddRange(
            new Company { LegalName = "TRINO FRIO ARMAZENS GERAIS LTDA", TaxId = "11111111000111" },
            new Company { LegalName = "TRINO LOGISTICA INTEGRADA LTDA", TaxId = "22222222000122", Active = false });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task A_empresa_da_SC_vem_do_cadastro_e_grava_o_nome_oficial()
    {
        var db = await ComEmpresasAsync();
        var (sc, erro) = await Servico(db).CreateAsync(Ana, "Reposição", "CC-01", null, null, [Item],
            header: Cabecalho("trino frio armazens gerais ltda"));
        Assert.Null(erro);
        Assert.Equal("TRINO FRIO ARMAZENS GERAIS LTDA", sc!.Company);
    }

    [Theory]
    [InlineData("Trino Frio")]                         // digitado livre
    [InlineData("TRINO LOGISTICA INTEGRADA LTDA")]     // cadastrada, mas inativa
    public async Task Empresa_fora_do_cadastro_ativo_e_recusada(string empresa)
    {
        var db = await ComEmpresasAsync();
        var (sc, erro) = await Servico(db).CreateAsync(Ana, "Reposição", "CC-01", null, null, [Item],
            header: Cabecalho(empresa));
        Assert.Null(sc);
        Assert.Equal("PR-ERR-023", erro!.Code);
    }

    [Fact]
    public async Task Empresa_continua_opcional()
    {
        var db = await ComEmpresasAsync();
        var (sc, erro) = await Servico(db).CreateAsync(Ana, "Reposição", "CC-01", null, null, [Item], header: Cabecalho(null));
        Assert.Null(erro);
        Assert.Null(sc!.Company);
    }

    [Fact]
    public async Task Sem_CNPJ_cadastrado_a_base_antiga_continua_criando_SC()
    {
        var (sc, erro) = await Servico(Banco()).CreateAsync(Ana, "Reposição", "CC-01", null, null, [Item],
            header: Cabecalho("Grupo Trino"));
        Assert.Null(erro);
        Assert.Equal("Grupo Trino", sc!.Company);
    }

    [Fact]
    public async Task Com_todos_os_CNPJs_inativos_vale_o_mesmo_que_sem_cadastro()
    {
        // a tela só enxerga as ativas: sem nenhuma, ela usa o nome do padrão da O.C., e o
        // servidor precisa aceitá-lo — senão nenhuma SC nasce
        var db = Banco();
        db.Companies.Add(new Company { LegalName = "ANTIGA LTDA", TaxId = "33333333000133", Active = false });
        await db.SaveChangesAsync();
        var (sc, erro) = await Servico(db).CreateAsync(Ana, "Reposição", "CC-01", null, null, [Item],
            header: Cabecalho("Grupo Trino"));
        Assert.Null(erro);
        Assert.Equal("Grupo Trino", sc!.Company);
    }
}
