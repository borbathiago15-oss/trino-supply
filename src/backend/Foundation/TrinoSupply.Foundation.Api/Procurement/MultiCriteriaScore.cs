namespace TrinoSupply.Foundation.Api.Procurement;

public record ScoreInput(
    Guid SupplierId, string SupplierName, decimal Total,
    int? DeliveryDays, int? PaymentDays, double? OtifPercent, int? RiskScore);

public record ScoreRow(
    Guid SupplierId, string SupplierName, double Score,
    double? PricePct, double? DeliveryPct, double? PaymentPct, double? OtifPct, double? RiskPct);

/// <summary>Um critério do score e quanto ele pesa, para a tela poder dizer de que o número é feito.</summary>
public record CriterioDoScore(string Code, string Label, double Weight, string Help);

/// <summary>
/// Score multicritério da escolha (V2-P4, decisão C5: INFORMATIVO — nunca decide nem bloqueia).
/// Compara as propostas mais recentes por preço, prazo de entrega, prazo de pagamento, OTIF
/// histórico do fornecedor e risco interno. Cada componente é normalizado contra o melhor da
/// disputa; componente sem dado sai da conta (pesos renormalizados).
///
/// <para>
/// <b>Os pesos vêm de fora.</b> Quem compara é a empresa, e a régua com que ela compara é dela:
/// os valores de <see cref="Padrao"/> são só o ponto de partida de quem ainda não definiu a sua.
/// </para>
/// </summary>
public static class MultiCriteriaScore
{
    /// <summary>
    /// Os critérios, com o peso de fábrica e a explicação de cada um.
    ///
    /// <para>
    /// A lista é publicada junto do resultado, e não escondida na conta, porque um score que não
    /// diz de que é feito é um palpite com cara de medida: quem lê precisa saber quanto vale
    /// cada coisa antes de decidir se concorda com a ordem.
    /// </para>
    ///
    /// <para>
    /// O <b>peso</b> daqui é apenas o padrão — a empresa troca o seu em <c>ScoreWeights</c>. O
    /// que não se troca é o resto: o código, o rótulo e a explicação são de quem escreveu a
    /// conta, não de quem configura a régua.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<CriterioDoScore> Padrao =
    [
        new("price", "Preço", 0.4, "O menor total da disputa vale 100; os outros caem na proporção."),
        new("delivery", "Prazo de entrega", 0.2, "O prazo mais curto vale 100."),
        new("payment", "Prazo de pagamento", 0.1, "O prazo mais longo vale 100 — pagar depois é caixa."),
        new("otif", "OTIF histórico", 0.2, "Entregas no prazo e completas do fornecedor nos últimos 12 meses."),
        new("risk", "Risco interno", 0.1, "100 menos o risco do scorecard: quanto maior, menos risco."),
    ];

    /// <param name="criterios">
    /// A régua a usar. Omitida, vale o padrão — é o que mantém honesto todo teste e toda
    /// chamada que não tem opinião sobre peso.
    /// </param>
    public static List<ScoreRow> Compute(
        IReadOnlyList<ScoreInput> inputs, IReadOnlyList<CriterioDoScore>? criterios = null)
    {
        var regua = criterios ?? Padrao;
        var validos = inputs.Where(i => i.Total > 0).ToList();
        if (validos.Count == 0) return [];

        var melhorPreco = validos.Min(i => i.Total);
        var prazos = validos.Where(i => i.DeliveryDays is > 0).Select(i => i.DeliveryDays!.Value).ToList();
        var menorPrazo = prazos.Count > 0 ? prazos.Min() : (int?)null;
        var pagamentos = validos.Where(i => i.PaymentDays is > 0).Select(i => i.PaymentDays!.Value).ToList();
        var maiorPagamento = pagamentos.Count > 0 ? pagamentos.Max() : (int?)null;

        var rows = validos.Select(i =>
        {
            var price = (double)(melhorPreco * 100 / i.Total);
            double? delivery = i.DeliveryDays is > 0 && menorPrazo is { } mp
                ? mp * 100.0 / i.DeliveryDays.Value : null;
            double? payment = i.PaymentDays is > 0 && maiorPagamento is { } mx
                ? i.PaymentDays.Value * 100.0 / mx : null;
            double? otif = i.OtifPercent;
            double? risk = i.RiskScore is { } r ? 100.0 - r : null;

            // os pesos saem da mesma régua que a tela recebe, e não de números soltos aqui:
            // publicar uma tabela na tela e usar outra na conta seria a pior forma de mentir —
            // a explicação pareceria conferida
            var partes = new List<(double valor, double peso)> { (price, Peso(regua, "price")) };
            if (delivery is not null) partes.Add((delivery.Value, Peso(regua, "delivery")));
            if (payment is not null) partes.Add((payment.Value, Peso(regua, "payment")));
            if (otif is not null) partes.Add((otif.Value, Peso(regua, "otif")));
            if (risk is not null) partes.Add((risk.Value, Peso(regua, "risk")));

            // critério desligado (peso 0) não entra: mantê-lo faria o denominador crescer sem
            // que o valor contasse, e o score cairia por causa de algo que a empresa dispensou
            partes = partes.Where(p => p.peso > 0).ToList();
            var soma = partes.Sum(p => p.peso);
            var score = soma > 0 ? Math.Round(partes.Sum(p => p.valor * p.peso) / soma, 1) : 0;

            return new ScoreRow(i.SupplierId, i.SupplierName, score,
                Math.Round(price, 1),
                delivery is null ? null : Math.Round(delivery.Value, 1),
                payment is null ? null : Math.Round(payment.Value, 1),
                otif is null ? null : Math.Round(otif.Value, 1),
                risk is null ? null : Math.Round(risk.Value, 1));
        }).OrderByDescending(r => r.Score).ToList();
        return rows;
    }

    private static double Peso(IReadOnlyList<CriterioDoScore> regua, string code) =>
        regua.FirstOrDefault(c => c.Code == code)?.Weight ?? 0;
}
