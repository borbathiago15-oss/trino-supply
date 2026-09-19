namespace TrinoSupply.Procurement.Domain;

/// <summary>
/// Fato de entrega de UMA linha de OC, já consolidado das entregas parciais que a atenderam.
/// É a matéria-prima do OTIF — sai do prazo prometido na OC e da conferência feita na doca.
/// </summary>
/// <param name="Period">Mês da última entrega da linha, no formato "2026-09".</param>
/// <param name="PromisedDate">Prazo prometido na OC. Nulo = não dá para medir pontualidade.</param>
/// <param name="LastReceivedAt">Data da entrega mais recente que atendeu esta linha.</param>
public sealed record DeliveryFact(
    Guid SupplierId, string Period, decimal QuantityOrdered, decimal QuantityReceived,
    decimal QuantityDamaged, bool HasOccurrence, DateOnly? PromisedDate, DateOnly LastReceivedAt)
{
    /// <summary>Chegou no prazo? Só mensurável quando a OC trouxe data prometida.</summary>
    public bool? OnTime => PromisedDate is null ? null : LastReceivedAt <= PromisedDate.Value;

    /// <summary>Veio completo e sem avaria? Compara o LÍQUIDO aproveitável com o que foi pedido.</summary>
    public bool InFull => QuantityReceived - QuantityDamaged >= QuantityOrdered;
}

/// <summary>Faixa de desempenho do fornecedor, derivada do índice OTIF.</summary>
public enum SupplierTier { SemDados = 0, Critico = 1, Bronze = 2, Prata = 3, Ouro = 4 }

/// <summary>
/// Desempenho de entrega de um fornecedor num período (OTIF — On Time In Full).
/// <para>
/// <b>Pontualidade</b> e <b>OTIF</b> só consideram linhas com prazo prometido na OC; as demais entram
/// em <see cref="LinesWithoutDeadline"/> em vez de serem contadas como pontuais — medir o que não foi
/// combinado infla a nota do fornecedor.
/// </para>
/// </summary>
public sealed record SupplierScore(
    Guid SupplierId, string Period, int LinesEvaluated, int LinesWithDeadline, int LinesWithoutDeadline,
    int OnTimeLines, int InFullLines, int OtifLines, int Occurrences,
    decimal QuantityReceived, decimal QuantityDamaged)
{
    private static decimal Pct(int parte, int total) =>
        total == 0 ? 0m : Math.Round(parte * 100m / total, 2, MidpointRounding.AwayFromZero);

    /// <summary>% de linhas entregues até o prazo (entre as que tinham prazo).</summary>
    public decimal OnTimeRate => Pct(OnTimeLines, LinesWithDeadline);

    /// <summary>% de linhas entregues completas e sem avaria.</summary>
    public decimal InFullRate => Pct(InFullLines, LinesEvaluated);

    /// <summary>% de linhas no prazo E completas — o índice clássico de OTIF.</summary>
    public decimal OtifIndex => Pct(OtifLines, LinesWithDeadline);

    /// <summary>% da quantidade recebida que chegou imprestável.</summary>
    public decimal DamageRate => QuantityReceived == 0m
        ? 0m
        : Math.Round(QuantityDamaged * 100m / QuantityReceived, 2, MidpointRounding.AwayFromZero);

    /// <summary>Faixa de desempenho. Sem linha com prazo, não há OTIF para classificar.</summary>
    public SupplierTier Tier => LinesWithDeadline == 0
        ? SupplierTier.SemDados
        : OtifIndex switch
        {
            >= 95m => SupplierTier.Ouro,
            >= 85m => SupplierTier.Prata,
            >= 70m => SupplierTier.Bronze,
            _ => SupplierTier.Critico,
        };
}

/// <summary>Cálculo do OTIF a partir dos fatos de entrega. Puro — sem banco, sem relógio.</summary>
public static class SupplierScorecard
{
    /// <summary>Consolida os fatos num score por (fornecedor, período).</summary>
    public static IReadOnlyList<SupplierScore> ByPeriod(IEnumerable<DeliveryFact> facts) =>
        facts.GroupBy(f => (f.SupplierId, f.Period))
            .Select(g => Consolidate(g.Key.SupplierId, g.Key.Period, g))
            .OrderBy(s => s.SupplierId).ThenByDescending(s => s.Period, StringComparer.Ordinal)
            .ToList();

    /// <summary>Consolida TODOS os fatos do fornecedor num score único (janela inteira).</summary>
    public static SupplierScore Overall(Guid supplierId, string period, IEnumerable<DeliveryFact> facts) =>
        Consolidate(supplierId, period, facts);

    private static SupplierScore Consolidate(Guid supplierId, string period, IEnumerable<DeliveryFact> facts)
    {
        var lista = facts.ToList();
        var comPrazo = lista.Where(f => f.OnTime is not null).ToList();

        return new SupplierScore(
            SupplierId: supplierId,
            Period: period,
            LinesEvaluated: lista.Count,
            LinesWithDeadline: comPrazo.Count,
            LinesWithoutDeadline: lista.Count - comPrazo.Count,
            OnTimeLines: comPrazo.Count(f => f.OnTime == true),
            InFullLines: lista.Count(f => f.InFull),
            OtifLines: comPrazo.Count(f => f.OnTime == true && f.InFull),
            Occurrences: lista.Count(f => f.HasOccurrence),
            QuantityReceived: lista.Sum(f => f.QuantityReceived),
            QuantityDamaged: lista.Sum(f => f.QuantityDamaged));
    }
}
