using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>Uma compra do produto: o que se pagou, de quem e quando.</summary>
public record PrecoPago(
    DateTimeOffset Em, Guid SupplierId, string SupplierName, string OrderNumber,
    decimal UnitPrice, decimal Quantity, string? Family);

/// <summary>
/// O que o histórico responde sobre um produto. <see cref="Medio"/> é <b>ponderado pela
/// quantidade</b>: mil unidades a 5 e uma a 50 não têm média 27,50 — a média que interessa
/// é a do dinheiro que saiu, não a das linhas da planilha.
/// </summary>
public record ResumoDePreco(
    Guid CatalogItemId, decimal Ultimo, DateTimeOffset UltimoEm, Guid UltimoFornecedorId,
    string UltimoFornecedor, decimal Medio, decimal Minimo, decimal Maximo,
    int Compras, int Fornecedores);

/// <summary>
/// Memória de preço do que a empresa comprou.
///
/// <para>
/// A pergunta "estamos pagando acima do que já pagamos?" não tinha onde ser feita: o preço
/// vivia solto em cada O.C., e a única consulta que o lia estava enterrada dentro do
/// registro do pedido, privada, devolvendo só o último valor. Este serviço é aquela
/// consulta tirada de lá e generalizada — mesma fonte, mesma regra, agora legível por
/// quem precisar.
/// </para>
///
/// <para>
/// A fonte é o <b>pedido</b>, não a proposta: proposta é o que foi oferecido, pedido é o
/// que foi comprado. Misturar os dois faria o histórico responder por preços que nunca
/// saíram do papel. Pedido cancelado fica de fora pelo mesmo motivo.
/// </para>
/// </summary>
public class HistoricoDePrecoService(AppDbContext db)
{
    /// <summary>Teto da série devolvida — histórico longo vira gráfico ilegível, não informação.</summary>
    public const int TetoDaSerie = 200;

    /// <summary>
    /// O que se pagou pelo produto, do mais recente para o mais antigo. Vazio para produto
    /// nunca comprado, que é um estado legítimo e não um erro.
    /// </summary>
    public async Task<IReadOnlyList<PrecoPago>> SerieAsync(Guid catalogItemId, CancellationToken ct = default) =>
        await Compras(db).Where(x => x.CatalogItemId == catalogItemId)
            .OrderByDescending(x => x.Em).Take(TetoDaSerie)
            .Select(x => new PrecoPago(x.Em, x.SupplierId, x.SupplierName, x.OrderNumber,
                x.UnitPrice, x.Quantity, x.Family))
            .ToListAsync(ct);

    /// <summary>
    /// Resumo de vários produtos de uma vez. É plural de propósito: o registro da O.C.
    /// precisa do preço anterior de todos os itens do pedido, e uma consulta por item
    /// transformaria uma compra de vinte linhas em vinte idas ao banco.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, ResumoDePreco>> ResumoAsync(
        IReadOnlyCollection<Guid> catalogItemIds, CancellationToken ct = default)
    {
        if (catalogItemIds.Count == 0) return new Dictionary<Guid, ResumoDePreco>();
        var ids = catalogItemIds.Distinct().ToList();

        // O agrupamento é em memória de propósito: são as compras dos produtos deste
        // pedido, não a base inteira, e a média ponderada com "o mais recente" junto sai
        // mais clara aqui do que espalhada em três consultas agregadas.
        var compras = await Compras(db).Where(x => ids.Contains(x.CatalogItemId)).ToListAsync(ct);

        return compras.GroupBy(x => x.CatalogItemId).ToDictionary(g => g.Key, g =>
        {
            var recente = g.OrderByDescending(x => x.Em).First();
            var quantidade = g.Sum(x => x.Quantity);
            // sem quantidade não há como ponderar; a média simples é o que sobra de honesto
            var medio = quantidade > 0
                ? g.Sum(x => x.UnitPrice * x.Quantity) / quantidade
                : g.Average(x => x.UnitPrice);
            return new ResumoDePreco(g.Key, recente.UnitPrice, recente.Em, recente.SupplierId,
                recente.SupplierName, Math.Round(medio, 4),
                g.Min(x => x.UnitPrice), g.Max(x => x.UnitPrice),
                g.Count(), g.Select(x => x.SupplierId).Distinct().Count());
        });
    }

    /// <summary>
    /// Variação do preço proposto contra a média já paga, em pontos percentuais. Positivo
    /// é acima do histórico.
    ///
    /// <para>
    /// Nula quando não há histórico ou quando a média é zero: sem base de comparação, um
    /// número aqui seria invenção — e é justamente o produto sem histórico que mais
    /// pareceria alarmante se a conta caísse para zero.
    /// </para>
    /// </summary>
    public static decimal? VariacaoPercentual(ResumoDePreco? historico, decimal precoProposto) =>
        historico is { Medio: > 0 }
            ? Math.Round((precoProposto / historico.Medio - 1) * 100m, 1)
            : null;

    /// <summary>
    /// Acima de quanto a variação merece aviso. Dez por cento é folgado de propósito:
    /// apertar demais faria a tela gritar em toda reposição com reajuste normal, e o aviso
    /// que grita sempre para de ser lido.
    /// </summary>
    public const decimal AvisoAcimaDePct = 10m;

    /// <summary>Merece aviso? Só variação para cima — pagar menos não é anomalia a resolver.</summary>
    public static bool MereceAviso(decimal? variacaoPct) => variacaoPct > AvisoAcimaDePct;

    /// <summary>
    /// A base do histórico: item de pedido com preço, de pedido não cancelado. Uma
    /// definição só, para o resumo e a série nunca discordarem sobre o que conta como
    /// compra.
    /// </summary>
    private static IQueryable<CompraDoProduto> Compras(AppDbContext db) =>
        from item in db.PurchaseOrderItems
        join pedido in db.PurchaseOrders on item.OrderId equals pedido.Id
        where item.CatalogItemId != null && item.UnitPrice != null
              && pedido.Status != PurchaseOrderStatus.Cancelled
        select new CompraDoProduto(item.CatalogItemId!.Value, pedido.CreatedAt, pedido.SupplierId,
            pedido.SupplierName, pedido.Number, item.UnitPrice!.Value, item.Quantity, item.Family);

    private record CompraDoProduto(
        Guid CatalogItemId, DateTimeOffset Em, Guid SupplierId, string SupplierName,
        string OrderNumber, decimal UnitPrice, decimal Quantity, string? Family);
}
