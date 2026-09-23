using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Acoes;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Insights;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Api.Melhoria;

/// <summary>
/// Quantas vezes um achado já apareceu — e, o que importa, <b>em quantos dias diferentes</b>.
///
/// <para>
/// Contar chamadas seria contar quem atualizou a tela: dez F5 na mesma tarde dariam um achado
/// "recorrente" que nasceu hoje. O que conta é o dia: o achado visto em três dias diferentes
/// deixou de ser acidente do dado de um dia só.
/// </para>
/// </summary>
public class InsightSighting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>A mesma chave do ciclo: <c>INSIGHT:código:assunto</c>.</summary>
    public string OriginKey { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public DateOnly FirstSeenOn { get; set; }
    public DateOnly LastSeenOn { get; set; }
    /// <summary>Dias <b>diferentes</b> em que o achado apareceu.</summary>
    public int Days { get; set; } = 1;
    /// <summary>O plano que o gatilho abriu, quando abriu. Nulo enquanto não abriu.</summary>
    public Guid? PlanId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// O gatilho: o achado que <b>não passa</b> abre o plano sozinho.
///
/// <para>
/// É a última peça do caminho do Trino Intelligence — lá o plano nasce quando um indicador
/// fica crítico por vários períodos seguidos. O problema que aparece uma vez pode ser ruído
/// do mês; o que volta em três dias diferentes é problema, e deixá-lo esperando alguém abrir
/// um plano na mão é como o defeito passa batido.
/// </para>
///
/// <para>
/// Ele é avaliado <b>quando alguém abre o Insights</b>, como o escalonamento de prazo e os
/// avisos do ciclo: o projeto não tem agendador, e um relógio de servidor entregaria o mesmo
/// recado com uma peça a mais que pode falhar em silêncio.
/// </para>
/// </summary>
public class GatilhoDePlanoService(AppDbContext db, AvisoDoUsuarioService avisos, TimeProvider clock)
{
    /// <summary>
    /// Dias diferentes a partir dos quais o achado vira plano. Três é o ponto em que ele
    /// deixa de ser acidente do dado de um dia: aparecer, sumir e voltar não conta.
    /// </summary>
    public const int DiasParaOGatilho = 3;

    /// <summary>
    /// Só achado de severidade <b>alta</b> abre plano. O gatilho que dispara com tudo enche a
    /// lista de planos que ninguém pediu, e a primeira coisa que se aprende é a ignorá-la.
    /// </summary>
    public const string SeveridadeQueDispara = "alta";

    /// <summary>Teto por avaliação — a tela não pode virar uma rotina de gravação em massa.</summary>
    public const int Teto = 50;

    public async Task<IReadOnlyList<ActionPlan>> AvaliarAsync(
        IReadOnlyList<Insight> achados, CancellationToken ct = default)
    {
        if (achados.Count == 0) return [];
        var agora = clock.GetUtcNow();
        var hoje = DateOnly.FromDateTime(agora.UtcDateTime);

        var chaves = achados.Take(Teto)
            .Select(a => CausaDoAchadoService.ChaveDoAchado(a.Code, a.Title))
            .Distinct().ToArray();
        var vistos = await db.InsightSightings.Where(s => chaves.Contains(s.OriginKey))
            .ToDictionaryAsync(s => s.OriginKey, ct);

        var abertos = new List<ActionPlan>();
        foreach (var achado in achados.Take(Teto))
        {
            var chave = CausaDoAchadoService.ChaveDoAchado(achado.Code, achado.Title);
            if (!vistos.TryGetValue(chave, out var visto))
            {
                visto = new InsightSighting
                {
                    OriginKey = chave, Code = achado.Code, Title = achado.Title,
                    Severity = achado.Severity, FirstSeenOn = hoje, LastSeenOn = hoje, Days = 1,
                    CreatedAt = agora, UpdatedAt = agora,
                };
                db.InsightSightings.Add(visto);
                vistos[chave] = visto;
                continue;   // achado de hoje não dispara nada: um dia não é recorrência
            }

            // o dia é o que conta. Dez atualizações da tela na mesma tarde não fazem um
            // achado recorrente — e era assim que o gatilho abriria plano no primeiro F5
            if (visto.LastSeenOn != hoje)
            {
                visto.LastSeenOn = hoje;
                visto.Days += 1;
                visto.UpdatedAt = agora;
            }
            visto.Severity = achado.Severity;
            visto.Title = achado.Title;

            if (visto.PlanId is not null) continue;
            if (visto.Days < DiasParaOGatilho) continue;
            if (!string.Equals(achado.Severity, SeveridadeQueDispara, StringComparison.OrdinalIgnoreCase))
                continue;

            var autoKey = $"insight:{chave}";
            if (autoKey.Length > 160) autoKey = autoKey[..160];
            // a chave de dedupe é o que impede o mesmo gatilho de abrir um plano por visita
            if (await db.ActionPlans.AnyAsync(p => p.AutoKey == autoKey, ct)) continue;

            var plano = new ActionPlan
            {
                Code = await ProximoCodigoAsync(agora, ct),
                Title = $"Contramedida: {achado.Title}",
                Problem = achado.Title,
                BusinessReason = string.IsNullOrWhiteSpace(achado.Action) ? achado.Evidence : achado.Action,
                Description = achado.Evidence,
                Priority = "ALTA", Criticality = "ALTA",
                AutoKey = autoKey,
                StartDate = hoje,
                CreatedBy = Guid.Empty,
                CreatedByLabel = "Gatilho automático",
                CreatedAt = agora, UpdatedAt = agora,
            };
            db.ActionPlans.Add(plano);
            visto.PlanId = plano.Id;
            abertos.Add(plano);

            // o plano nasce SEM responsável, de propósito: pendurá-lo em quem por acaso
            // abriu a tela seria dar trabalho a um sorteado. Quem recebe o recado é quem
            // distribui — e recado sem dono é recado que ninguém lê
            foreach (var gestor in await avisos.GestoresAsync(ct))
                avisos.Enfileirar(gestor, AvisoDoCicloKinds.PlanoPorGatilho,
                    $"{plano.Code}: plano aberto por gatilho",
                    $"O achado \"{achado.Title}\" apareceu em {visto.Days} dias diferentes. "
                    + "O plano foi aberto sem responsável — escolha quem toca.",
                    $"gatilho-plano:{plano.Id}", "/plano-acao/" + plano.Id);
        }

        await db.SaveChangesAsync(ct);
        return abertos;
    }

    private async Task<string> ProximoCodigoAsync(DateTimeOffset agora, CancellationToken ct)
    {
        var prefixo = $"AP-{agora.Year}-";
        var usados = await db.ActionPlans.Where(p => p.Code.StartsWith(prefixo))
            .Select(p => p.Code).ToListAsync(ct);
        var novos = db.ActionPlans.Local.Where(p => p.Code.StartsWith(prefixo)).Select(p => p.Code);
        var maior = usados.Concat(novos)
            .Select(n => int.TryParse(n[prefixo.Length..], out var v) ? v : 0)
            .DefaultIfEmpty(0).Max();
        return $"{prefixo}{maior + 1:000}";
    }
}
