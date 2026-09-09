using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// A memória de preço da empresa. O que estes testes protegem é o que faria o histórico
/// mentir: contar proposta como compra, contar pedido cancelado, e a média simples fingir
/// que mil unidades baratas pesam o mesmo que uma cara.
/// </summary>
public class HistoricoDePrecoServiceTests
{
    private static readonly Guid Bota = Guid.NewGuid();
    private static readonly Guid Alfa = Guid.NewGuid();
    private static readonly Guid Beta = Guid.NewGuid();

    private static (AppDbContext Db, HistoricoDePrecoService Svc) Build()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (db, new HistoricoDePrecoService(db));
    }

    private static void Comprar(AppDbContext db, decimal preco, decimal quantidade, int dia,
        Guid? fornecedor = null, PurchaseOrderStatus status = PurchaseOrderStatus.Issued,
        Guid? produto = null)
    {
        var pedido = new PurchaseOrder
        {
            Number = $"PO-2026-{dia:000000}", Status = status,
            SupplierId = fornecedor ?? Alfa,
            SupplierName = fornecedor == Beta ? "Beta" : "Alfa",
            CreatedAt = new DateTimeOffset(2026, 1, dia, 12, 0, 0, TimeSpan.Zero),
        };
        pedido.Items.Add(new PurchaseOrderItem
        {
            OrderId = pedido.Id, Description = "Bota", UnitOfMeasure = "PAR",
            Quantity = quantidade, UnitPrice = preco,
            CatalogItemId = produto ?? Bota, Family = "EPI",
            CreatedAt = pedido.CreatedAt,
        });
        db.PurchaseOrders.Add(pedido);
        db.SaveChanges();
    }

    [Fact]
    public async Task Produto_nunca_comprado_devolve_vazio_e_nao_erro()
    {
        // "nunca compramos isto" é resposta legítima; um erro aqui faria a tela do
        // produto novo parecer quebrada
        var (_, svc) = Build();
        Assert.Empty(await svc.SerieAsync(Bota));
        Assert.Empty(await svc.ResumoAsync([Bota]));
    }

    [Fact]
    public async Task A_media_e_ponderada_pela_quantidade()
    {
        // mil a 5 e uma a 50 não têm média 27,50: a média que interessa é a do dinheiro
        // que saiu, não a das linhas da planilha
        var (db, svc) = Build();
        Comprar(db, preco: 5m, quantidade: 1000, dia: 1);
        Comprar(db, preco: 50m, quantidade: 1, dia: 2);

        var resumo = (await svc.ResumoAsync([Bota]))[Bota];
        Assert.Equal(5.045m, resumo.Medio);      // (5000 + 50) / 1001
        Assert.Equal(5m, resumo.Minimo);
        Assert.Equal(50m, resumo.Maximo);
        Assert.Equal(2, resumo.Compras);
    }

    [Fact]
    public async Task Pedido_cancelado_nao_conta_como_compra()
    {
        // compra desfeita não é preço praticado; contá-la puxaria a média para um valor
        // que a empresa nunca pagou
        var (db, svc) = Build();
        Comprar(db, preco: 10m, quantidade: 1, dia: 1);
        Comprar(db, preco: 90m, quantidade: 1, dia: 2, status: PurchaseOrderStatus.Cancelled);

        var resumo = (await svc.ResumoAsync([Bota]))[Bota];
        Assert.Equal(10m, resumo.Medio);
        Assert.Equal(1, resumo.Compras);
        Assert.Equal(10m, resumo.Ultimo);
    }

    [Fact]
    public async Task O_ultimo_preco_e_o_do_pedido_mais_recente_com_o_fornecedor()
    {
        var (db, svc) = Build();
        Comprar(db, preco: 10m, quantidade: 1, dia: 1, fornecedor: Alfa);
        Comprar(db, preco: 12m, quantidade: 1, dia: 5, fornecedor: Beta);

        var resumo = (await svc.ResumoAsync([Bota]))[Bota];
        Assert.Equal(12m, resumo.Ultimo);
        Assert.Equal(Beta, resumo.UltimoFornecedorId);
        Assert.Equal("Beta", resumo.UltimoFornecedor);
        Assert.Equal(2, resumo.Fornecedores);
    }

    [Fact]
    public async Task A_serie_vem_do_mais_recente_para_o_mais_antigo()
    {
        var (db, svc) = Build();
        Comprar(db, preco: 10m, quantidade: 1, dia: 1);
        Comprar(db, preco: 11m, quantidade: 1, dia: 9);
        Comprar(db, preco: 12m, quantidade: 1, dia: 5);

        var serie = await svc.SerieAsync(Bota);
        Assert.Equal([11m, 12m, 10m], serie.Select(p => p.UnitPrice));
    }

    [Fact]
    public async Task O_resumo_de_varios_produtos_nao_mistura_um_com_o_outro()
    {
        // é a consulta que o registro da O.C. usa: vinte itens num pedido não podem virar
        // vinte idas ao banco, nem um preço vazar para o produto do lado
        var (db, svc) = Build();
        var luva = Guid.NewGuid();
        Comprar(db, preco: 10m, quantidade: 1, dia: 1, produto: Bota);
        Comprar(db, preco: 99m, quantidade: 1, dia: 1, produto: luva);

        var resumo = await svc.ResumoAsync([Bota, luva]);
        Assert.Equal(10m, resumo[Bota].Ultimo);
        Assert.Equal(99m, resumo[luva].Ultimo);
    }

    // ---- variação contra o histórico ----------------------------------------

    [Fact]
    public void Sem_historico_a_variacao_e_nula_em_vez_de_zero()
    {
        // zero diria "está no preço de sempre" para um produto que nunca foi comprado —
        // é a diferença entre não saber e afirmar
        Assert.Null(HistoricoDePrecoService.VariacaoPercentual(null, 100m));
        Assert.False(HistoricoDePrecoService.MereceAviso(null));
    }

    [Fact]
    public void A_variacao_compara_com_a_media_e_avisa_so_para_cima()
    {
        var historico = new ResumoDePreco(Bota, 100m, DateTimeOffset.UtcNow, Alfa, "Alfa",
            Medio: 100m, Minimo: 90m, Maximo: 110m, Compras: 3, Fornecedores: 1);

        // o exemplo do documento: média 100, novo preço 140 → 40%
        Assert.Equal(40m, HistoricoDePrecoService.VariacaoPercentual(historico, 140m));
        Assert.True(HistoricoDePrecoService.MereceAviso(40m));

        // reajuste pequeno não grita: o aviso que grita sempre para de ser lido
        Assert.False(HistoricoDePrecoService.MereceAviso(
            HistoricoDePrecoService.VariacaoPercentual(historico, 105m)));

        // pagar menos não é anomalia a resolver
        Assert.Equal(-20m, HistoricoDePrecoService.VariacaoPercentual(historico, 80m));
        Assert.False(HistoricoDePrecoService.MereceAviso(-20m));
    }
}
