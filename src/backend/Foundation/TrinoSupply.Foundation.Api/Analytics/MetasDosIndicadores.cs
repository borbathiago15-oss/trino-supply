using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Analytics;

/// <summary>
/// A meta de um indicador, como a empresa a definiu: um valor <b>mensal</b> por indicador.
///
/// <para>
/// <b>Sem meta cadastrada não há comparação</b> — nunca uma meta inventada. Um card que diz
/// "85% da meta" sobre um número que ninguém definiu parece conferido e não é; o card sem meta
/// mostra só o valor. Por isso a linha só existe quando alguém grava, e apagar a meta é gravar
/// vazio: o indicador volta a não ter comparação.
/// </para>
/// </summary>
public class IndicatorGoal
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>A chave do indicador no catálogo (<see cref="MetasDosIndicadores.Catalogo"/>).</summary>
    public string Indicator { get; set; } = string.Empty;

    /// <summary>O valor para um mês. No período, o indicador que acumula a multiplica pelos meses.</summary>
    public decimal MonthlyValue { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedByLabel { get; set; } = string.Empty;
}

/// <summary>
/// Um indicador que aceita meta. <paramref name="MaiorEMelhor"/> diz o sentido — saving e OTIF
/// querem subir, gasto e prazo querem descer. <paramref name="Acumula"/> diz se o número do
/// período é soma (a meta mensal cresce com o período) ou média/taxa (a meta é a mesma).
/// </summary>
public record IndicadorComMeta(string Codigo, string Rotulo, string Unidade, bool MaiorEMelhor, bool Acumula);

/// <summary>O valor do período diante da meta do período.</summary>
public record ComparacaoComMeta(string Indicador, decimal MetaDoPeriodo, decimal Valor, double Atingimento, string Faixa);

public static class MetasDosIndicadores
{
    /// <summary>
    /// Os indicadores que aceitam meta. A chave é a mesma de <see cref="AnalyticsService.DefinicoesDosIndicadores"/>
    /// quando o indicador está no painel: a meta é do número que o card mostra, não de outro parecido.
    /// </summary>
    public static readonly IReadOnlyList<IndicadorComMeta> Catalogo =
    [
        new("poTotalValue", "Valor comprado (teto)", "moeda", MaiorEMelhor: false, Acumula: true),
        new("saving", "Saving negociado", "moeda", MaiorEMelhor: true, Acumula: true),
        new("otif", "Entrega no prazo (OTIF)", "pct", MaiorEMelhor: true, Acumula: false),
        new("avgApprovalDays", "Tempo médio de aprovação", "dias", MaiorEMelhor: false, Acumula: false),
        new("avgReceiveDays", "Tempo médio de entrega", "dias", MaiorEMelhor: false, Acumula: false),
        new("overdue", "SCs em atraso", "qtd", MaiorEMelhor: false, Acumula: false),
    ];

    public static IndicadorComMeta? Do(string codigo) => Catalogo.FirstOrDefault(i => i.Codigo == codigo);

    /// <summary>
    /// Quantos meses o período tem, para a meta que acumula. Pelos dias, e não pelos meses
    /// tocados: de 1 a 15 de setembro é meio mês, e cobrar a meta do mês inteiro faria toda
    /// quinzena parecer abaixo da meta.
    /// </summary>
    public static decimal MesesDoPeriodo(DateOnly de, DateOnly ate) =>
        Math.Round((ate.DayNumber - de.DayNumber + 1) / 30.4375m, 2);

    /// <summary>
    /// A régua única da comparação: o painel e a diretoria perguntam aqui. No sentido "maior é
    /// melhor", bate a meta a partir de 100% e fica em atenção a partir de 80%; no "menor é
    /// melhor", bate até a meta e fica em atenção até 20% acima dela. Sem valor medido não há
    /// comparação — "não medido" não é "abaixo da meta".
    /// </summary>
    public static ComparacaoComMeta? Comparar(IndicadorComMeta indicador, decimal metaMensal, decimal? valor, decimal meses)
    {
        if (valor is null || metaMensal <= 0) return null;
        var meta = indicador.Acumula ? Math.Round(metaMensal * meses, 2) : metaMensal;
        if (meta <= 0) return null;
        var atingimento = Math.Round((double)(valor.Value / meta * 100), 1);
        string faixa = indicador.MaiorEMelhor
            ? atingimento >= 100 ? "ok" : atingimento >= 80 ? "atencao" : "fora"
            : atingimento <= 100 ? "ok" : atingimento <= 120 ? "atencao" : "fora";
        return new(indicador.Codigo, meta, valor.Value, atingimento, faixa);
    }
}

/// <summary>A meta que o administrador enviou: vazio apaga.</summary>
public record MetaInput(string Indicator, decimal? MonthlyValue);

public class MetasDosIndicadoresService(AppDbContext db, TimeProvider clock)
{
    public static bool CanEdit(string role) => role == Roles.SystemAdministrator;

    public async Task<IReadOnlyDictionary<string, IndicatorGoal>> AtuaisAsync(CancellationToken ct = default) =>
        (await db.IndicatorGoals.AsNoTracking().ToListAsync(ct)).ToDictionary(g => g.Indicator);

    /// <summary>
    /// Compara os valores do período com as metas cadastradas. Só volta o que tem meta e valor
    /// medido: o indicador ausente do dicionário é o que a tela mostra sem comparação.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ComparacaoComMeta>> CompararAsync(
        IReadOnlyDictionary<string, decimal?> valores, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var metas = await AtuaisAsync(ct);
        var meses = MetasDosIndicadores.MesesDoPeriodo(de, ate);
        var resultado = new Dictionary<string, ComparacaoComMeta>();
        foreach (var (codigo, valor) in valores)
        {
            if (MetasDosIndicadores.Do(codigo) is not { } indicador || !metas.TryGetValue(codigo, out var meta)) continue;
            if (MetasDosIndicadores.Comparar(indicador, meta.MonthlyValue, valor, meses) is { } c) resultado[codigo] = c;
        }
        return resultado;
    }

    public async Task<(IReadOnlyDictionary<string, IndicatorGoal>? metas, UserError? error)> SalvarAsync(
        Actor actor, IReadOnlyList<MetaInput> pedido, CancellationToken ct = default)
    {
        if (!CanEdit(actor.Role))
            return (null, new("MET-ERR-900", "Só o administrador define as metas dos indicadores."));
        foreach (var m in pedido)
        {
            if (MetasDosIndicadores.Do(m.Indicator) is null)
                return (null, new("MET-ERR-010", $"\"{m.Indicator}\" não é um indicador que aceita meta."));
            if (m.MonthlyValue is <= 0)
                return (null, new("MET-ERR-011",
                    "A meta precisa ser maior que zero. Para tirar a meta de um indicador, deixe o campo vazio."));
            if (MetasDosIndicadores.Do(m.Indicator)!.Unidade == "pct" && m.MonthlyValue > 100)
                return (null, new("MET-ERR-012", "Meta em percentual vai até 100."));
        }

        var agora = clock.GetUtcNow();
        var gravadas = await db.IndicatorGoals.ToListAsync(ct);
        foreach (var m in pedido)
        {
            var atual = gravadas.FirstOrDefault(g => g.Indicator == m.Indicator);
            if (m.MonthlyValue is null)
            {
                if (atual is not null) db.IndicatorGoals.Remove(atual);
                continue;
            }
            if (atual is null)
            {
                atual = new IndicatorGoal { Indicator = m.Indicator };
                db.IndicatorGoals.Add(atual);
            }
            if (atual.MonthlyValue == m.MonthlyValue.Value && db.Entry(atual).State != EntityState.Added) continue;
            atual.MonthlyValue = m.MonthlyValue.Value;
            atual.UpdatedAt = agora;
            atual.UpdatedByLabel = actor.Label;
        }
        await db.SaveChangesAsync(ct);
        return (await AtuaisAsync(ct), null);
    }
}
