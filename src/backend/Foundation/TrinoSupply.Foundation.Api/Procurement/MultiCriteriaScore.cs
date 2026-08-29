namespace TrinoSupply.Foundation.Api.Procurement;

public record ScoreInput(
    Guid SupplierId, string SupplierName, decimal Total,
    int? DeliveryDays, int? PaymentDays, double? OtifPercent, int? RiskScore);

public record ScoreRow(
    Guid SupplierId, string SupplierName, double Score,
    double? PricePct, double? DeliveryPct, double? PaymentPct, double? OtifPct, double? RiskPct);

/// <summary>
/// Score multicritério da escolha (V2-P4, decisão C5: INFORMATIVO — nunca decide nem bloqueia).
/// Compara as propostas mais recentes: preço (peso 40), prazo de entrega (20), prazo de
/// pagamento (10), OTIF histórico do fornecedor (20) e risco interno (10). Cada componente é
/// normalizado contra o melhor da disputa; componente sem dado sai da conta (pesos renormalizados).
/// </summary>
public static class MultiCriteriaScore
{
    public static List<ScoreRow> Compute(IReadOnlyList<ScoreInput> inputs)
    {
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

            var partes = new List<(double valor, double peso)> { (price, 0.4) };
            if (delivery is not null) partes.Add((delivery.Value, 0.2));
            if (payment is not null) partes.Add((payment.Value, 0.1));
            if (otif is not null) partes.Add((otif.Value, 0.2));
            if (risk is not null) partes.Add((risk.Value, 0.1));
            var score = Math.Round(partes.Sum(p => p.valor * p.peso) / partes.Sum(p => p.peso), 1);

            return new ScoreRow(i.SupplierId, i.SupplierName, score,
                Math.Round(price, 1),
                delivery is null ? null : Math.Round(delivery.Value, 1),
                payment is null ? null : Math.Round(payment.Value, 1),
                otif is null ? null : Math.Round(otif.Value, 1),
                risk is null ? null : Math.Round(risk.Value, 1));
        }).OrderByDescending(r => r.Score).ToList();
        return rows;
    }
}
