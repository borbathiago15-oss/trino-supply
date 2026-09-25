using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O item fora do catálogo na cotação (RFQ-ERR-026). Cotar pode; comprar, não: o item vira
/// produto antes da escolha do vencedor, como o fornecedor pré-cadastrado precisa estar
/// homologado para vencer. O orçamento fecha com item digitado e é cobrado quando vira compra.
/// Estes testes montam o banco <b>sem</b> o <see cref="ItensDaCotacaoNoCatalogo"/> — é a regra
/// que eles verificam.
/// </summary>
public class ProdutoForaDoCatalogoTests
{
    private sealed class Relogio : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class Numeros : IPrNumberGenerator
    {
        private int _n;
        public Task<string> NextAsync(CancellationToken ct = default) => Task.FromResult($"PR-2026-{++_n:000000}");
    }

    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana Solicitante", Roles.Requester);
    private static readonly Actor Carla = new(Guid.NewGuid(), "Carla Compradora", Roles.PurchasingOfficer);

    private sealed record Mundo(AppDbContext Db, QuotationService Rfq, RequisitionService Prs, CatalogService Catalogo, Supplier Alfa);

    private static async Task<Mundo> Montar()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var relogio = new Relogio();
        var sup = new SupplierService(db, relogio);
        var (alfa, _) = await sup.CreateAsync(Carla.Id, "Alfa LTDA", "Alfa", "12345678000190", null, "81 3333-1000");
        await sup.SetHomologationAsync(alfa!.Id, SupplierHomologation.Homologado);
        var catalogo = new CatalogService(db, relogio);
        return new(db, new QuotationService(db, relogio),
            new RequisitionService(db, new Numeros(), catalogo, relogio), catalogo, alfa);
    }

    /// <summary>SC com um item digitado, cotada e fechada para análise — pronta para a escolha.</summary>
    private static async Task<Quotation> CotadaAsync(Mundo m, string finalidade = FinalidadeDaSc.Compra)
    {
        var (pr, erro) = await m.Prs.CreateAsync(Ana, "Suporte para o monitor da recepção", "CC-01", "NORMAL", null,
            [new ItemInput("Suporte articulado de monitor", 2, "UN", 180, null)],
            header: new RequisitionService.ScHeaderInput(null, null, null, null, Purpose: finalidade));
        Assert.Null(erro);
        await m.Prs.SubmitAsync(Ana, pr!.Id);
        var (q, e1) = await m.Rfq.CreateFromPrAsync(Carla, pr.Id, QuotationKind.Purchase, null, null);
        Assert.Null(e1);
        await m.Rfq.InviteSuppliersAsync(Carla, q!.Id, [m.Alfa.Id]);
        var (_, e2) = await m.Rfq.SubmitProposalAsync(q.Id, m.Alfa.Id,
            new ProposalInput(10, "28 dias", 0, null, null, [new ProposalItemInput(q.Items.Single().Id, 175m, null)]), "PORTAL", "Alfa");
        Assert.Null(e2);
        await m.Rfq.CloseForAnalysisAsync(Carla, q.Id);
        return (await m.Rfq.GetAsync(q.Id))!;
    }

    private static Task<(Quotation? q, TrinoSupply.Foundation.Api.Users.UserError? error)> Escolher(Mundo m, Quotation q) =>
        m.Rfq.SelectWinnerAsync(Carla, q.Id, q.Proposals.Single().Id, null, "Único fornecedor com o suporte articulado");

    private static async Task<CatalogItem> Produto(Mundo m, string codigo = "MOB-010", string familia = "MOBILIARIO")
    {
        var (p, erro) = await m.Catalogo.CreateAsync(Carla.Id, codigo, "Suporte articulado para monitor 17 a 32 pol.", familia, "UN", 180m);
        Assert.Null(erro);
        return p!;
    }

    [Fact]
    public async Task Item_fora_do_catalogo_cota_mas_nao_deixa_escolher_o_vencedor()
    {
        var m = await Montar();
        var q = await CotadaAsync(m);
        Assert.Null(q.Items.Single().CatalogItemId);

        var (escolhido, erro) = await Escolher(m, q);
        Assert.Null(escolhido);
        Assert.Equal("RFQ-ERR-026", erro!.Code);
        // o comprador precisa saber qual item cadastrar
        Assert.Contains("Suporte articulado de monitor", erro.Message);
    }

    [Fact]
    public async Task Com_o_produto_definido_o_item_vira_o_do_catalogo_e_a_escolha_segue()
    {
        var m = await Montar();
        var q = await CotadaAsync(m);
        var produto = await Produto(m);
        var item = q.Items.Single();

        var (depois, erro) = await m.Rfq.DefinirProdutoDoItemAsync(Carla, q.Id, item.Id, produto.Id);
        Assert.Null(erro);
        var agora = depois!.Items.Single();
        Assert.Equal(produto.Id, agora.CatalogItemId);
        Assert.Equal("MOB-010", agora.CatalogCode);
        Assert.Equal(produto.Description, agora.Description);
        Assert.Equal("MOBILIARIO", agora.Family);
        // a quantidade e a unidade ficam: a proposta foi dada sobre elas
        Assert.Equal(2, agora.Quantity);

        // a SC de origem ganha o mesmo vínculo: a Torre e o histórico passam a ver o produto
        var daSc = await m.Db.RequisitionItems.SingleAsync(r => r.Id == item.SourcePrItemId);
        Assert.Equal(produto.Id, daSc.CatalogItemId);
        Assert.Equal("MOB-010", daSc.CatalogCode);

        var evento = await m.Db.ProcessEvents.SingleAsync(e => e.QuotationId == q.Id && e.EventType == "PRODUTO_DO_ITEM");
        Assert.Contains("Suporte articulado de monitor", evento.Description);
        Assert.Contains("MOB-010", evento.Description);

        var (escolhido, erroEscolha) = await Escolher(m, depois);
        Assert.Null(erroEscolha);
        Assert.Equal(QuotationStatus.AwaitingManager, escolhido!.Status);
    }

    [Fact]
    public async Task Orcamento_fecha_com_item_digitado_e_a_cobranca_vem_ao_virar_compra()
    {
        var m = await Montar();
        var q = await CotadaAsync(m, FinalidadeDaSc.Orcamento);

        var (apresentado, erro) = await Escolher(m, q);
        Assert.Null(erro);
        Assert.Equal(QuotationStatus.BudgetPresented, apresentado!.Status);

        var (_, erroConverter) = await m.Rfq.ConverterOrcamentoEmCompraAsync(Carla, q.Id);
        Assert.Equal("RFQ-ERR-026", erroConverter!.Code);

        var produto = await Produto(m);
        var familiaDoLote = apresentado.Items.Single().Family;
        var (vinculado, erroVinculo) = await m.Rfq.DefinirProdutoDoItemAsync(Carla, q.Id, apresentado.Items.Single().Id, produto.Id);
        Assert.Null(erroVinculo);
        // a adjudicação por família já está feita: mudar a família tiraria o item do lote que o fornecedor levou
        Assert.Equal(familiaDoLote, vinculado!.Items.Single().Family);

        var (compra, e3) = await m.Rfq.ConverterOrcamentoEmCompraAsync(Carla, q.Id);
        Assert.Null(e3);
        Assert.Equal(QuotationStatus.AwaitingManager, compra!.Status);
    }

    [Fact]
    public async Task O_vinculo_e_recusado_fora_da_etapa_duas_vezes_com_produto_inativo_e_para_quem_nao_compra()
    {
        var m = await Montar();
        var q = await CotadaAsync(m);
        var item = q.Items.Single();

        var inativo = await Produto(m, "MOB-099");
        inativo.Active = false;
        await m.Db.SaveChangesAsync();
        Assert.Equal("IC-ERR-032", (await m.Rfq.DefinirProdutoDoItemAsync(Carla, q.Id, item.Id, inativo.Id)).error!.Code);

        Assert.Equal("RFQ-ERR-900", (await m.Rfq.DefinirProdutoDoItemAsync(Ana, q.Id, item.Id, inativo.Id)).error!.Code);
        Assert.Equal("RFQ-ERR-023", (await m.Rfq.DefinirProdutoDoItemAsync(Carla, q.Id, Guid.NewGuid(), inativo.Id)).error!.Code);

        var produto = await Produto(m);
        Assert.Null((await m.Rfq.DefinirProdutoDoItemAsync(Carla, q.Id, item.Id, produto.Id)).error);
        // de novo no mesmo item: já é produto
        Assert.Equal("RFQ-ERR-027", (await m.Rfq.DefinirProdutoDoItemAsync(Carla, q.Id, item.Id, produto.Id)).error!.Code);

        var (escolhido, _) = await Escolher(m, (await m.Rfq.GetAsync(q.Id))!);
        Assert.Equal(QuotationStatus.AwaitingManager, escolhido!.Status);
        // depois da escolha o item já não muda de produto
        var (_, tarde) = await m.Rfq.ImpedimentoDoProdutoAsync(Carla, q.Id, item.Id);
        Assert.Equal("RFQ-ERR-027", tarde!.Code);
    }

    [Fact]
    public void O_comprador_cadastra_produto_mas_nao_mexe_na_estrutura_do_catalogo()
    {
        Assert.True(CatalogService.CanRegisterProduct(Roles.PurchasingOfficer));
        Assert.True(CatalogService.CanRegisterProduct(Roles.SupplyManager));
        Assert.False(CatalogService.CanRegisterProduct(Roles.Requester));
        // famílias, importação e excluir continuam do gestor e do administrador
        Assert.False(CatalogService.CanMaintain(Roles.PurchasingOfficer));
        Assert.Contains(AppModules.Produtos, AppModules.DefaultsFor(Roles.PurchasingOfficer));
    }
}
