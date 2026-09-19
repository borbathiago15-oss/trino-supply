namespace TrinoSupply.Procurement.Domain;

/// <summary>Natureza da divergência apontada pela conciliação das três pontas.</summary>
public enum DivergenceKind
{
    Preco = 1,              // preço faturado difere do acordado na OC
    Quantidade = 2,         // faturou mais do que foi pedido ou do que chegou em condições
    Avaria = 3,             // chegou material danificado (bloqueia se a nota não o descontou)
    ItemNaoEncontrado = 4,  // item da nota que não existe na OC
}

/// <summary>
/// Tolerâncias da conciliação. Preço tem folga pequena (arredondamento de centavos em nota com
/// muitas casas); quantidade, por padrão, nenhuma — faturar mais do que chegou não é arredondamento.
/// </summary>
public sealed record MatchTolerance(decimal PricePercent, decimal QuantityPercent)
{
    public static MatchTolerance Default => new(0.5m, 0m);
}

/// <summary>O que a OC negociou para um item.</summary>
public sealed record OrderedFact(string ItemCode, decimal Quantity, decimal UnitPrice);

/// <summary>O que a NF-e faturou para um item (o código é o do fornecedor, como vem no XML).</summary>
public sealed record InvoicedFact(string ItemCode, decimal Quantity, decimal UnitPrice);

/// <summary>O que a doca conferiu: o que chegou íntegro e o que chegou danificado.</summary>
public sealed record ReceivedFact(string ItemCode, decimal NetQuantity, decimal DamagedQuantity);

/// <summary>Uma divergência concreta, com o esperado e o encontrado para caber num relatório.</summary>
public sealed record MatchDivergence(
    string ItemCode, DivergenceKind Kind, decimal Expected, decimal Found,
    decimal DeviationPercent, bool WithinTolerance, string Message)
{
    /// <summary>Código estável para alerta/integração (ex.: <c>DIV-ERR-PRECO</c>).</summary>
    public string Code => $"DIV-ERR-{Kind.ToString().ToUpperInvariant()}";
}

/// <summary>Um item nas três colunas: pedido, faturado e recebido, com o veredito.</summary>
public sealed record MatchLine(
    string ItemCode,
    decimal QuantityOrdered, decimal UnitPriceOrdered,
    decimal? QuantityInvoiced, decimal? UnitPriceInvoiced,
    decimal QuantityReceived, decimal QuantityDamaged,
    IReadOnlyList<MatchDivergence> Divergences)
{
    /// <summary>Linha conciliada: sem divergência, ou só com divergências dentro da tolerância.</summary>
    public bool IsMatched => Divergences.All(d => d.WithinTolerance);

    /// <summary>Item que esta nota não faturou (entrega/faturamento parcial é normal).</summary>
    public bool NotInvoiced => QuantityInvoiced is null;
}

public sealed record MatchResult(IReadOnlyList<MatchLine> Lines)
{
    public bool Matched => Lines.All(l => l.IsMatched);
    public IReadOnlyList<MatchDivergence> Divergences =>
        Lines.SelectMany(l => l.Divergences).Where(d => !d.WithinTolerance).ToList();

    /// <summary>Resumo de uma linha para gravar junto da nota e mostrar no alerta.</summary>
    public string Summary => Matched
        ? "Conciliado"
        : string.Join(" · ", Divergences.Select(d => $"{d.Code} em {d.ItemCode}"));
}

/// <summary>
/// Conciliação de três pontas (Fase 05): <b>o que foi negociado</b> (OC) × <b>o que foi faturado</b>
/// (NF-e) × <b>o que efetivamente chegou em condições</b> (conferência da doca, MMS-005). A regra de
/// ouro do financeiro cabe numa frase: <i>não se paga o que não foi pedido, nem o que não chegou
/// bom</i>.
/// <para>
/// Cálculo puro — sem banco, sem relógio, sem I/O — então cada regra é testável isoladamente e o
/// resultado é reproduzível a partir dos mesmos três fatos.
/// </para>
/// <para>
/// O casamento item a item é feito pelo <b>código do produto</b>. O código que vem no XML é o do
/// fornecedor: enquanto não existir um de-para fornecedor↔item, o que ele chamar diferente cai como
/// <see cref="DivergenceKind.ItemNaoEncontrado"/> — visível e resolvível, em vez de silencioso.
/// </para>
/// </summary>
public static class ThreeWayMatch
{
    public static MatchResult Run(
        IEnumerable<OrderedFact> ordered, IEnumerable<InvoicedFact> invoiced,
        IEnumerable<ReceivedFact> received, MatchTolerance? tolerance = null)
    {
        var tol = tolerance ?? MatchTolerance.Default;

        var pedidos = Consolidate(ordered, o => o.ItemCode,
            g => (Quantity: g.Sum(x => x.Quantity), UnitPrice: g.Max(x => x.UnitPrice)));
        var notas = Consolidate(invoiced, i => i.ItemCode,
            g => (Quantity: g.Sum(x => x.Quantity), UnitPrice: g.Max(x => x.UnitPrice)));
        var docas = Consolidate(received, r => r.ItemCode,
            g => (Net: g.Sum(x => x.NetQuantity), Damaged: g.Sum(x => x.DamagedQuantity)));

        var linhas = new List<MatchLine>();

        foreach (var (code, pedido) in pedidos)
        {
            var temNota = notas.TryGetValue(code, out var nota);
            docas.TryGetValue(code, out var doca);

            var divergencias = new List<MatchDivergence>();

            if (temNota)
            {
                divergencias.AddRange(ChecarPreco(code, pedido.UnitPrice, nota.UnitPrice, tol));
                divergencias.AddRange(ChecarQuantidade(code, pedido.Quantity, nota.Quantity, doca.Net, tol));
            }
            divergencias.AddRange(ChecarAvaria(code, doca.Damaged, temNota ? nota.Quantity : null, doca.Net));

            linhas.Add(new MatchLine(
                code, pedido.Quantity, pedido.UnitPrice,
                temNota ? nota.Quantity : null, temNota ? nota.UnitPrice : null,
                doca.Net, doca.Damaged, divergencias));
        }

        // Item que veio na nota e não existe na OC: faturamento a mais, sempre bloqueia.
        foreach (var (code, nota) in notas.Where(n => !pedidos.ContainsKey(n.Key)))
        {
            docas.TryGetValue(code, out var doca);
            linhas.Add(new MatchLine(
                code, 0m, 0m, nota.Quantity, nota.UnitPrice, doca.Net, doca.Damaged,
                [new MatchDivergence(code, DivergenceKind.ItemNaoEncontrado, 0m, nota.Quantity, 100m, false,
                    $"Item '{code}' está na nota fiscal mas não foi pedido nesta OC.")]));
        }

        return new MatchResult(linhas.OrderBy(l => l.ItemCode, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static IEnumerable<MatchDivergence> ChecarPreco(
        string code, decimal acordado, decimal faturado, MatchTolerance tol)
    {
        if (acordado <= 0 || faturado == acordado) yield break;

        var desvio = Round(Math.Abs(faturado - acordado) / acordado * 100m);
        var dentro = desvio <= tol.PricePercent;
        yield return new MatchDivergence(code, DivergenceKind.Preco, acordado, faturado, desvio, dentro,
            $"Item '{code}': faturado a {faturado:N4}, acordado {acordado:N4} ({desvio:N2}% de desvio).");
    }

    private static IEnumerable<MatchDivergence> ChecarQuantidade(
        string code, decimal pedida, decimal faturada, decimal recebidaLiquida, MatchTolerance tol)
    {
        // Só o excesso importa: faturar menos é faturamento parcial, e isso é normal.
        var teto = Math.Min(pedida, recebidaLiquida);
        if (faturada <= teto) yield break;

        var desvio = teto > 0 ? Round((faturada - teto) / teto * 100m) : 100m;
        var dentro = desvio <= tol.QuantityPercent;
        var motivo = faturada > pedida && faturada > recebidaLiquida
            ? "passa do pedido e do recebido"
            : faturada > pedida ? "passa do pedido na OC" : "passa do que chegou em condições";
        yield return new MatchDivergence(code, DivergenceKind.Quantidade, teto, faturada, desvio, dentro,
            $"Item '{code}': faturadas {faturada:N4} — {motivo} ({teto:N4}).");
    }

    private static IEnumerable<MatchDivergence> ChecarAvaria(
        string code, decimal avariada, decimal? faturada, decimal liquida)
    {
        if (avariada <= 0) yield break;

        // Avaria só trava o pagamento se a nota NÃO a tiver descontado. Se o fornecedor já faturou
        // apenas o que chegou bom, a ocorrência segue viva na tratativa, mas não segura o financeiro.
        var descontada = faturada is null || faturada <= liquida;
        yield return new MatchDivergence(code, DivergenceKind.Avaria, 0m, avariada, 100m, descontada,
            descontada
                ? $"Item '{code}': {avariada:N4} avariada(s) na doca — a nota já não as cobra."
                : $"Item '{code}': {avariada:N4} avariada(s) na doca e a nota as está cobrando.");
    }

    private static Dictionary<string, TValue> Consolidate<TSource, TValue>(
        IEnumerable<TSource> source, Func<TSource, string> key, Func<IGrouping<string, TSource>, TValue> select) =>
        source.GroupBy(x => key(x).Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, select, StringComparer.OrdinalIgnoreCase);

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
