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

    // ---- concentração de fornecedor -----------------------------------------

    private static void Catalogo(AppDbContext db, Guid id, string descricao)
    {
        db.CatalogItems.Add(new TrinoSupply.Foundation.Api.Catalog.CatalogItem
        {
            Id = id, Code = "C-" + descricao[..3], Description = descricao,
            Family = "EPI", UnitOfMeasure = "PAR",
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task Produto_comprado_uma_vez_nao_vira_alarme_de_dependencia()
    {
        // 100% concentrado por aritmética, não por dependência. Sem este piso a tela
        // encheria de alarme falso e o alarme verdadeiro se perderia no meio
        var (db, svc) = Build();
        Catalogo(db, Bota, "Bota");
        Comprar(db, preco: 10m, quantidade: 1, dia: 1);

        Assert.Empty(await svc.ConcentracaoAsync());
    }

    [Fact]
    public async Task Fornecedor_unico_e_critico_e_a_recomendacao_diz_o_que_fazer()
    {
        var (db, svc) = Build();
        Catalogo(db, Bota, "Bota");
        for (var dia = 1; dia <= 4; dia++) Comprar(db, preco: 10m, quantidade: 1, dia: dia);

        var risco = Assert.Single(await svc.ConcentracaoAsync());
        Assert.Equal("CRITICO", risco.Nivel);
        Assert.Equal(1, risco.Fornecedores);
        Assert.Contains("Fornecedor único", risco.Recomendacao);
        // a recomendação nomeia quem é: "identifique uma alternativa" sem dizer a quem
        // não ajuda ninguém a agir
        Assert.Contains("Alfa", risco.Recomendacao);
    }

    [Fact]
    public async Task A_fatia_e_sobre_o_valor_comprado_e_nao_sobre_o_numero_de_pedidos()
    {
        // dez compras pequenas num fornecedor e uma enorme noutro não fazem do primeiro
        // o dono da conta
        var (db, svc) = Build();
        Catalogo(db, Bota, "Bota");
        for (var dia = 1; dia <= 3; dia++) Comprar(db, preco: 1m, quantidade: 1, dia: dia, fornecedor: Alfa);
        Comprar(db, preco: 1000m, quantidade: 1, dia: 9, fornecedor: Beta);

        var risco = Assert.Single(await svc.ConcentracaoAsync());
        Assert.Equal(Beta, risco.Maior.SupplierId);       // 1000 de 1003
        Assert.Equal(99.7m, risco.Maior.Pct);
        Assert.Equal("CRITICO", risco.Nivel);
        // e a contagem de compras do maior é a dele, não a do produto: Beta comprou 1 vez
        Assert.Equal(1, risco.Maior.Compras);
        Assert.Equal(4, risco.Compras);        // o produto foi comprado 4 vezes ao todo
    }

    [Fact]
    public async Task Compra_bem_dividida_nao_aparece_na_lista()
    {
        // a lista é de risco, não de inventário: produto com fornecedores equilibrados
        // não tem o que ser feito a respeito
        var (db, svc) = Build();
        Catalogo(db, Bota, "Bota");
        Comprar(db, preco: 10m, quantidade: 1, dia: 1, fornecedor: Alfa);
        Comprar(db, preco: 10m, quantidade: 1, dia: 2, fornecedor: Beta);
        Comprar(db, preco: 10m, quantidade: 1, dia: 3, fornecedor: Alfa);
        Comprar(db, preco: 10m, quantidade: 1, dia: 4, fornecedor: Beta);

        Assert.Empty(await svc.ConcentracaoAsync());
    }

    [Fact]
    public async Task Entre_setenta_e_noventa_por_cento_e_atencao_e_nao_critico()
    {
        var (db, svc) = Build();
        Catalogo(db, Bota, "Bota");
        // Alfa 80, Beta 20 → 80%
        Comprar(db, preco: 80m, quantidade: 1, dia: 1, fornecedor: Alfa);
        Comprar(db, preco: 10m, quantidade: 1, dia: 2, fornecedor: Beta);
        Comprar(db, preco: 10m, quantidade: 1, dia: 3, fornecedor: Beta);

        var risco = Assert.Single(await svc.ConcentracaoAsync());
        Assert.Equal("ATENCAO", risco.Nivel);
        Assert.Equal(80m, risco.Maior.Pct);
    }

    [Fact]
    public async Task Pedido_cancelado_nao_conta_na_concentracao()
    {
        // mesma régua do preço: compra desfeita não cria dependência
        var (db, svc) = Build();
        Catalogo(db, Bota, "Bota");
        Comprar(db, preco: 10m, quantidade: 1, dia: 1, fornecedor: Alfa);
        Comprar(db, preco: 10m, quantidade: 1, dia: 2, fornecedor: Alfa);
        Comprar(db, preco: 10m, quantidade: 1, dia: 3, fornecedor: Alfa);
        Comprar(db, preco: 900m, quantidade: 1, dia: 4, fornecedor: Beta,
            status: PurchaseOrderStatus.Cancelled);

        var risco = Assert.Single(await svc.ConcentracaoAsync());
        Assert.Equal(Alfa, risco.Maior.SupplierId);
        Assert.Equal(1, risco.Fornecedores);
    }
}
