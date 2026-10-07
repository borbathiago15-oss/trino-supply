using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// Quem aparece em <b>Solicitar Material</b>: a família manda, o produto ajusta.
///
/// <para>
/// O que estes testes protegem é o que a tela mostrava antes — o catálogo inteiro, porque o
/// único flag que existia (<c>StockControlled</c>) está <c>true</c> em toda linha do banco — e o
/// que um erro aqui causaria depois: a tela esvaziar sozinha, ou o produto que foi marcado como
/// exceção sumir junto com a família dele.
/// </para>
/// </summary>
public class MaterialDoAlmoxarifadoTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Agora = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.NewGuid();

    private static CatalogService Build(out AppDbContext db)
    {
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return new CatalogService(db, new FixedTimeProvider(Agora));
    }

    private static async Task<ProductFamily> FamiliaAsync(
        CatalogService svc, string nome, bool deAlmoxarifado)
    {
        var (familia, erro) = await svc.CreateFamilyAsync(Admin, nome, null, materialRequestable: deAlmoxarifado);
        Assert.Null(erro);
        return familia!;
    }

    private static async Task<CatalogItem> ProdutoAsync(
        CatalogService svc, string codigo, string familia, string? ajuste = null)
    {
        // com custo: o produto de almoxarifado não nasce sem ele (IC-ERR-018), e a regra tem teste próprio
        var (item, erro) = await svc.CreateAsync(Admin, codigo, $"Produto {codigo}", familia, "UN", 10m,
            materialAdjust: ajuste);
        Assert.Null(erro);
        return item!;
    }

    // ---- a regra, na frase ------------------------------------------------------

    [Fact]
    public void Sem_nada_dito_o_produto_entra()
    {
        // é o que todo registro anterior à regra significa, e recusar em silêncio o que
        // sempre apareceu seria pior que mostrar demais
        Assert.True(MaterialDoAlmoxarifado.Entra(doProduto: null, daFamilia: null));
    }

    [Fact]
    public void A_familia_manda_quando_o_produto_nao_se_manifesta()
    {
        Assert.True(MaterialDoAlmoxarifado.Entra(null, daFamilia: true));
        Assert.False(MaterialDoAlmoxarifado.Entra(null, daFamilia: false));
    }

    [Fact]
    public void O_ajuste_do_produto_vence_a_familia_nos_dois_sentidos()
    {
        // o papel A4 que não sai do estoque dentro de uma família que sai, e o contrário
        Assert.False(MaterialDoAlmoxarifado.Entra(doProduto: false, daFamilia: true));
        Assert.True(MaterialDoAlmoxarifado.Entra(doProduto: true, daFamilia: false));
    }

    [Fact]
    public void O_ajuste_viaja_como_texto_porque_ausente_e_familia_seriam_o_mesmo_nulo()
    {
        Assert.Null(MaterialDoAlmoxarifado.Ajuste(MaterialDoAlmoxarifado.SegueAFamilia));
        Assert.True(MaterialDoAlmoxarifado.Ajuste(MaterialDoAlmoxarifado.SempreEntra));
        Assert.False(MaterialDoAlmoxarifado.Ajuste(MaterialDoAlmoxarifado.NuncaEntra));
        // texto desconhecido não inventa exceção: segue a família
        Assert.Null(MaterialDoAlmoxarifado.Ajuste("qualquer coisa"));
        Assert.Equal(MaterialDoAlmoxarifado.SegueAFamilia, MaterialDoAlmoxarifado.Texto(null));
    }

    // ---- a mesma regra, na consulta ---------------------------------------------

    [Fact]
    public async Task O_recorte_da_tela_segue_a_familia_e_respeita_a_excecao()
    {
        var svc = Build(out _);
        await FamiliaAsync(svc, "EPI", deAlmoxarifado: true);
        await FamiliaAsync(svc, "SERVICOS", deAlmoxarifado: false);
        await ProdutoAsync(svc, "EPI-001", "EPI");                                           // segue: entra
        await ProdutoAsync(svc, "EPI-002", "EPI", MaterialDoAlmoxarifado.NuncaEntra);        // exceção: fica fora
        await ProdutoAsync(svc, "SRV-001", "SERVICOS");                                      // segue: fica fora
        await ProdutoAsync(svc, "SRV-002", "SERVICOS", MaterialDoAlmoxarifado.SempreEntra);  // exceção: entra

        var naTela = await svc.ListAsync(null, null, false, materialOnly: true);

        Assert.Equal(["EPI-001", "SRV-002"], naTela.Select(i => i.Code).OrderBy(c => c));
    }

    [Fact]
    public async Task As_duas_formas_da_regra_concordam_item_a_item()
    {
        // `Entra` e `Filtro` são a mesma frase dita duas vezes — uma para a memória, outra para o
        // SQL. Separadas, a tela mostraria um conjunto e a conta diria outro
        var svc = Build(out var db);
        await FamiliaAsync(svc, "EPI", deAlmoxarifado: true);
        await FamiliaAsync(svc, "SERVICOS", deAlmoxarifado: false);
        foreach (var (codigo, familia, ajuste) in new (string, string, string?)[]
                 {
                     ("IT-A", "EPI", null), ("IT-B", "EPI", MaterialDoAlmoxarifado.NuncaEntra),
                     ("IT-C", "SERVICOS", null), ("IT-D", "SERVICOS", MaterialDoAlmoxarifado.SempreEntra),
                     ("IT-E", "FORA DO CADASTRO", null),
                 })
        {
            if (familia == "FORA DO CADASTRO")
            {
                // família que não está no cadastro: o produto entra pelo padrão
                db.CatalogItems.Add(new CatalogItem
                {
                    Code = codigo, Description = codigo, Family = familia,
                    CreatedAt = Agora, UpdatedAt = Agora, CreatedBy = Admin,
                });
                await db.SaveChangesAsync();
            }
            else await ProdutoAsync(svc, codigo, familia, ajuste);
        }

        var familias = await db.ProductFamilies.ToDictionaryAsync(f => f.Name, f => f.MaterialRequestable);
        var porConsulta = (await svc.ListAsync(null, null, false, materialOnly: true))
            .Select(i => i.Code).OrderBy(c => c).ToList();
        var porFrase = (await db.CatalogItems.ToListAsync())
            .Where(i => MaterialDoAlmoxarifado.Entra(
                i.MaterialRequestable, familias.TryGetValue(i.Family, out var f) ? f : null))
            .Select(i => i.Code).OrderBy(c => c).ToList();

        Assert.Equal(porFrase, porConsulta);
        Assert.Contains("IT-E", porConsulta);   // o padrão não é "fica de fora"
    }

    [Fact]
    public async Task A_familia_nasce_de_almoxarifado_para_a_tela_em_uso_nao_esvaziar()
    {
        // o campo chega a uma tela em uso: começar desmarcado tiraria todo mundo de Solicitar
        // Material no deploy, e quem tira um grupo de circulação é o cadastro, não a migration
        var svc = Build(out _);
        var (familia, erro) = await svc.CreateFamilyAsync(Admin, "MATERIAL DE LIMPEZA", null);

        Assert.Null(erro);
        Assert.True(familia!.MaterialRequestable);
    }

    [Fact]
    public async Task A_lista_de_familias_da_tela_traz_so_as_de_almoxarifado()
    {
        var svc = Build(out _);
        await FamiliaAsync(svc, "EPI", deAlmoxarifado: true);
        await FamiliaAsync(svc, "SERVICOS", deAlmoxarifado: false);

        var naTela = await svc.ListFamiliesAsync(false, materialOnly: true);

        Assert.Equal(["EPI"], naTela.Select(f => f.Name));
    }

    [Fact]
    public async Task Editar_o_produto_sem_mandar_o_ajuste_nao_mexe_no_que_esta_gravado()
    {
        // "não mandei o campo" e "volte a seguir a família" são decisões diferentes, e é por
        // isso que o ajuste viaja como texto em vez de booleano anulável
        var svc = Build(out _);
        await FamiliaAsync(svc, "EPI", deAlmoxarifado: true);
        var item = await ProdutoAsync(svc, "EPI-001", "EPI", MaterialDoAlmoxarifado.NuncaEntra);

        var (semMexer, _) = await svc.UpdateAsync(item.Id, "Outra descrição", null, null, null, null);
        Assert.False(semMexer!.MaterialRequestable);

        var (voltando, _) = await svc.UpdateAsync(item.Id, null, null, null, null, null,
            materialAdjust: MaterialDoAlmoxarifado.SegueAFamilia);
        Assert.Null(voltando!.MaterialRequestable);
    }
}
