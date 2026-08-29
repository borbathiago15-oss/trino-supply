namespace TrinoSupply.Foundation.Api.Analytics;

public record BuyerInput(string Label, double? OtifPercent, double? AvgDaysToPo, decimal SavingTotal, decimal PoValue);
public record BuyerScoreRow(string Label, double? Score, double? OtifPct, double? SpeedPct, double? SavingPct);

/// <summary>
/// Score composto de compradores (V2-P4, item 28 — habilitado com SLA e OTIF maduros).
/// Informativo, relativo ao período: entrega no prazo (OTIF, peso 40), agilidade até a O.C.
/// (peso 40, normalizada contra o mais rápido) e ganho de negociação sobre o valor comprado
/// (peso 20, normalizado contra o melhor). Componente sem dado sai da conta.
/// </summary>
public static class BuyerScore
{
    public static List<BuyerScoreRow> Compute(IReadOnlyList<BuyerInput> inputs)
    {
        var prazos = inputs.Where(i => i.AvgDaysToPo is not null).Select(i => i.AvgDaysToPo!.Value).ToList();
        var maisRapido = prazos.Count > 0 ? prazos.Min() : (double?)null;
        var taxas = inputs.Where(i => i.PoValue > 0)
            .Select(i => (double)(i.SavingTotal / i.PoValue)).ToList();
        var melhorTaxa = taxas.Count > 0 ? taxas.Max() : 0;

        return inputs.Select(i =>
        {
            double? speed = i.AvgDaysToPo is { } dias && maisRapido is { } melhor
                ? (melhor + 1) / (dias + 1) * 100 : null;   // +1 suaviza e cobre prazo zero
            double? saving = i.PoValue > 0 && melhorTaxa > 0
                ? (double)(i.SavingTotal / i.PoValue) / melhorTaxa * 100 : null;

            var partes = new List<(double valor, double peso)>();
            if (i.OtifPercent is not null) partes.Add((i.OtifPercent.Value, 0.4));
            if (speed is not null) partes.Add((speed.Value, 0.4));
            if (saving is not null) partes.Add((saving.Value, 0.2));
            double? score = partes.Count > 0
                ? Math.Round(partes.Sum(p => p.valor * p.peso) / partes.Sum(p => p.peso), 1) : null;

            return new BuyerScoreRow(i.Label, score,
                i.OtifPercent, speed is null ? null : Math.Round(speed.Value, 1),
                saving is null ? null : Math.Round(saving.Value, 1));
        }).ToList();
    }
}
