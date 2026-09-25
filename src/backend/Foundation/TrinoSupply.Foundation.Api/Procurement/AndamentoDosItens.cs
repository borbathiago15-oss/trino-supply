namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// Qual processo e qual pedido são de <b>cada item</b> da SC. A Torre é por item porque itens da
/// mesma SC andam em ritmos diferentes — e desde que o comprador pode seguir com parte da SC e
/// deixar o resto pendente, isso deixou de ser raro: a cotação leva a caneta e o grampeador fica
/// na Solicitação. Resolver pela SC mostrava o grampeador "em cotação" num processo onde ele não
/// está, e o comprador não tinha como achá-lo para cotar depois.
///
/// <para>
/// A cotação do item é a que o contém (<see cref="QuotationItem.SourcePrItemId"/>). O processo
/// antigo, aberto da SC inteira antes do rastreio por item, não tem essa marca em item nenhum e
/// continua valendo para todos os itens da SC — é o que ele sempre significou. O pedido é o da
/// cotação do item, de preferência o que traz o próprio item (a compra dividida gera um por
/// fornecedor); sem cotação, só o pedido lançado direto da SC, sem processo.
/// </para>
///
/// <para>
/// Lista, KPIs e cockpit perguntam a esta mesma classe: se cada um resolvesse do seu jeito, a TV
/// e a Torre voltariam a discordar no primeiro item que seguiu sozinho.
/// </para>
/// </summary>
public sealed class AndamentoDosItens
{
    private readonly Dictionary<Guid, Quotation> _porItem = new();
    private readonly Dictionary<Guid, Quotation> _legadoPorSc = new();
    private readonly Dictionary<Guid, List<PurchaseOrder>> _pedidosPorCotacao = new();
    private readonly Dictionary<Guid, PurchaseOrder> _pedidoDiretoPorSc = new();

    /// <param name="cotacoes">Do mais novo para o mais antigo: o item recotado fica com o processo novo.</param>
    /// <param name="pedidos">Também do mais novo para o mais antigo.</param>
    public AndamentoDosItens(IEnumerable<Quotation> cotacoes, IEnumerable<PurchaseOrder> pedidos)
    {
        foreach (var q in cotacoes)
        {
            var rastreados = q.Items.Where(i => i.SourcePrItemId is not null).ToList();
            foreach (var i in rastreados) _porItem.TryAdd(i.SourcePrItemId!.Value, q);
            if (rastreados.Count == 0) _legadoPorSc.TryAdd(q.SourcePrId, q);
        }
        foreach (var o in pedidos)
        {
            if (o.QuotationId is { } qid)
            {
                if (!_pedidosPorCotacao.TryGetValue(qid, out var lista)) _pedidosPorCotacao[qid] = lista = [];
                lista.Add(o);
            }
            else if (o.SourcePrId is { } pid) _pedidoDiretoPorSc.TryAdd(pid, o);
        }
    }

    public (Quotation? Cotacao, PurchaseOrder? Pedido) Do(PurchaseRequisition sc, RequisitionItem item)
    {
        var cotacao = _porItem.GetValueOrDefault(item.Id) ?? _legadoPorSc.GetValueOrDefault(sc.Id);
        if (cotacao is null) return (null, _pedidoDiretoPorSc.GetValueOrDefault(sc.Id));
        var pedidos = _pedidosPorCotacao.GetValueOrDefault(cotacao.Id);
        if (pedidos is null) return (cotacao, null);
        var doItem = pedidos.FirstOrDefault(o => o.Items.Any(i =>
            (item.CatalogItemId is not null && i.CatalogItemId == item.CatalogItemId) || i.Description == item.Description));
        return (cotacao, doItem ?? pedidos[0]);
    }
}
