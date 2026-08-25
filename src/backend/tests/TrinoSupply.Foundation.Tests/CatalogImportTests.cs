using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Tests;

/// <summary>Importação de produtos por planilha, com as planilhas reais do cliente.</summary>
public class CatalogImportTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly Guid Actor = Guid.NewGuid();

    private static (CatalogImportService svc, AppDbContext db) Build()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new AppDbContext(options);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));
        return (new CatalogImportService(db, clock), db);
    }

    private static List<string[]> ReadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", name);
        using var stream = File.OpenRead(path);
        return SpreadsheetReader.Read(stream, name);
    }

    [Fact]
    public void Leitor_de_xlsx_devolve_codigo_e_descricao()
    {
        var rows = ReadFixture("Cadastro_EPI.xlsx");
        Assert.True(rows.Count > 100);
        Assert.Equal("Produto", rows[0][0]);
        Assert.Equal("Descrição", rows[0][1]);
        Assert.Equal("21001", rows[1][0]);
        Assert.Equal("BALACLAVA", rows[1][1]);
    }

    [Fact]
    public async Task Preview_nao_grava_e_confirmacao_grava()
    {
        var (svc, db) = Build();
        var rows = ReadFixture("Cadastro_EPI.xlsx");
        var options = new ImportOptions("EPI", ProductTypes.Epi, StockControlled: true,
            Purchasable: true, MinimumQty: 5, Unit: "UN", Sizes: null);

        var (preview, e1) = await svc.ImportAsync(Actor, rows, options, commit: false);
        Assert.Null(e1);
        Assert.Equal(126, preview!.ToCreate);
        Assert.False(preview.Committed);
        Assert.Equal(0, await db.CatalogItems.CountAsync());
        Assert.Contains(preview.Warnings, w => w.Contains("C.A."));   // avisa a pendência de EPI

        var (commit, e2) = await svc.ImportAsync(Actor, rows, options, commit: true);
        Assert.Null(e2);
        Assert.Equal(126, commit!.ToCreate);
        Assert.Equal(126, await db.CatalogItems.CountAsync());
        var balaclava = await db.CatalogItems.SingleAsync(i => i.Code == "21001");
        Assert.Equal("BALACLAVA", balaclava.Description);
        Assert.Equal("EPI", balaclava.Family);
        Assert.True(balaclava.StockControlled);
        Assert.Equal(5m, balaclava.MinimumQty);
    }

    [Fact]
    public async Task Reimportar_a_mesma_planilha_nao_duplica()
    {
        var (svc, db) = Build();
        var rows = ReadFixture("Cadastro_Fardamento.xlsx");
        var options = new ImportOptions("FARDAMENTO", ProductTypes.Fardamento, true, true, null, "UN", null);

        await svc.ImportAsync(Actor, rows, options, commit: true);
        var total = await db.CatalogItems.CountAsync();

        var (again, _) = await svc.ImportAsync(Actor, rows, options, commit: true);
        Assert.Equal(0, again!.ToCreate);
        Assert.Equal(total, again.Duplicates);
        Assert.Equal(total, await db.CatalogItems.CountAsync());
    }

    [Fact]
    public async Task Grade_de_tamanhos_gera_uma_variante_por_tamanho()
    {
        var (svc, db) = Build();
        var rows = ReadFixture("Cadastro_Fardamento.xlsx");
        var options = new ImportOptions("FARDAMENTO", ProductTypes.Fardamento, true, true, 2m, "UN",
            Sizes: new[] { "P", "M", "G", "GG" });

        var (result, error) = await svc.ImportAsync(Actor, rows, options, commit: true);
        Assert.Null(error);
        Assert.Equal(43 * 4, result!.ToCreate);            // 43 modelos × 4 tamanhos

        var camisa = await db.CatalogItems.Where(i => i.BaseCode == "12003").OrderBy(i => i.Code).ToListAsync();
        Assert.Equal(4, camisa.Count);
        Assert.Equal(new[] { "12003-G", "12003-GG", "12003-M", "12003-P" }, camisa.Select(i => i.Code));
        Assert.All(camisa, i => Assert.StartsWith("CAMISA DE MALHA PV MANGA CURTA", i.Description));
        Assert.Contains(camisa, i => i.Size == "P" && i.Description.EndsWith("Tam. P"));
    }

    [Fact]
    public async Task Planilha_csv_tambem_e_aceita()
    {
        var (svc, db) = Build();
        var csv = "Produto;Descrição\n1001;PARAFUSO SEXTAVADO 1/2\n1002;PORCA SEXTAVADA 1/2\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var rows = SpreadsheetReader.Read(stream, "itens.csv");

        var (result, error) = await svc.ImportAsync(Actor, rows,
            new ImportOptions("MRO", ProductTypes.Mro, false, true, null, "UN", null), commit: true);
        Assert.Null(error);
        Assert.Equal(2, result!.ToCreate);
        Assert.Equal(2, await db.CatalogItems.CountAsync());
    }

    [Fact]
    public async Task Familia_fora_do_cadastro_bloqueia_a_importacao()
    {
        var (svc, db) = Build();
        db.ProductFamilies.Add(new ProductFamily { Name = "EPI", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var rows = ReadFixture("Cadastro_EPI.xlsx");
        var (_, error) = await svc.ImportAsync(Actor, rows,
            new ImportOptions("FARDAMENTO", null, true, true, null, "UN", null), commit: true);
        Assert.Equal("IC-ERR-022", error!.Code);
    }

    [Fact]
    public async Task Planilha_sem_as_colunas_esperadas_e_recusada()
    {
        var (svc, _) = Build();
        var rows = new List<string[]> { new[] { "Coluna X", "Coluna Y" }, new[] { "a", "b" } };
        var (_, error) = await svc.ImportAsync(Actor, rows,
            new ImportOptions("EPI", null, true, true, null, "UN", null), commit: false);
        Assert.Equal("IMP-ERR-012", error!.Code);
    }

    [Fact]
    public async Task Linhas_invalidas_sao_reportadas_sem_travar_o_arquivo()
    {
        var (svc, db) = Build();
        var rows = new List<string[]>
        {
            new[] { "Produto", "Descrição" },
            new[] { "9001", "ITEM BOM" },
            new[] { "", "SEM CÓDIGO" },
            new[] { "9003", "AB" },
            new[] { "9004", "OUTRO ITEM BOM" },
        };
        var (result, error) = await svc.ImportAsync(Actor, rows,
            new ImportOptions("MRO", null, false, true, null, "UN", null), commit: true);
        Assert.Null(error);
        Assert.Equal(2, result!.ToCreate);
        Assert.Equal(2, result.Errors);
        Assert.Equal(2, await db.CatalogItems.CountAsync());
    }
}
