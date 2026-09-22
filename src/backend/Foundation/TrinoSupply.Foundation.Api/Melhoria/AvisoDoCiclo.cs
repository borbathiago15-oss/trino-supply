using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Api.Melhoria;

/// <summary>
/// Os avisos do ciclo de melhoria, para o <b>dono</b> dele.
///
/// <para>
/// O módulo era 100% <i>pull</i>: um ciclo parado em Plan há dois meses não gritava em lugar
/// nenhum, e o Check vencia sem ninguém medir. São os dois fatos que o dono precisa saber sem
/// ir procurar.
/// </para>
///
/// <para>
/// A avaliação acontece <b>quando a pessoa abre a caixa</b>, como o escalonamento de prazo que
/// já existe: o projeto não tem agendador, e um relógio de servidor entregaria o mesmo recado
/// com uma peça a mais que pode falhar em silêncio. A <c>DedupeKey</c> é o que impede o aviso
/// de renascer a cada visita.
/// </para>
/// </summary>
public class AvisoDoCicloService(AppDbContext db, AvisoDoUsuarioService avisos, TimeProvider clock)
{
    /// <summary>
    /// A partir de quantos dias sem toque o ciclo conta como parado. Um mês é o intervalo em
    /// que um PDCA sem mexida deixou de ser lento e passou a ser esquecido.
    /// </summary>
    public const int DiasParado = 30;

    /// <summary>Teto por avaliação: caixa que enche de uma vez é caixa que ninguém lê.</summary>
    public const int Teto = 20;

    public async Task AvaliarAsync(Guid dono, CancellationToken ct = default)
    {
        if (dono == Guid.Empty) return;
        var agora = clock.GetUtcNow();
        var hoje = DateOnly.FromDateTime(agora.UtcDateTime);
        var corte = agora.AddDays(-DiasParado);

        // filtro na entidade e projeção por último: é o que o Npgsql traduz
        var meus = await db.ImprovementCycles
            .Where(c => c.OwnerId == dono && c.Phase != FaseDoCiclo.Encerrado)
            .OrderBy(c => c.UpdatedAt)
            .Take(Teto)
            .Select(c => new
            {
                c.Id, c.Code, c.Title, c.Phase, c.UpdatedAt, c.GoalDeadline, c.ResultValue,
            })
            .ToListAsync(ct);

        var emitiu = false;
        foreach (var c in meus)
        {
            // o Check vencido vem primeiro: ele tem data, e o parado é só ausência de toque
            if (c.GoalDeadline is { } prazo && prazo < hoje && c.ResultValue is null)
            {
                avisos.Enfileirar(dono, AvisoDoCicloKinds.CheckVencido,
                    $"{c.Code}: o prazo da meta venceu e ninguém mediu",
                    $"\"{c.Title}\" tinha prazo em {prazo:dd/MM/yyyy}. "
                    + "O prazo não encerra o ciclo — quem encerra é você, com o veredito.",
                    // a chave inclui o prazo: prazo novo é fato novo, e merece aviso novo
                    $"pdca-check-vencido:{c.Id}:{prazo:yyyy-MM-dd}",
                    $"/melhoria/{c.Id}");
                emitiu = true;
                continue;
            }

            if (c.UpdatedAt < corte)
            {
                var dias = (int)(agora - c.UpdatedAt).TotalDays;
                avisos.Enfileirar(dono, AvisoDoCicloKinds.CicloParado,
                    $"{c.Code}: parado há {dias} dias",
                    $"\"{c.Title}\" está em {FaseDoCiclo.Rotulo(c.Phase)} e não é tocado desde "
                    + $"{c.UpdatedAt:dd/MM/yyyy}.",
                    // a faixa de meses no lugar do dia exato: sem ela, o mesmo ciclo parado
                    // renderia um aviso por dia e a caixa viraria ruído
                    $"pdca-parado:{c.Id}:{dias / DiasParado}",
                    $"/melhoria/{c.Id}");
                emitiu = true;
            }
        }

        // nada aqui interrompe o fluxo: o aviso é sobre o fato, e o fato vale mais que o recado
        if (emitiu) await db.SaveChangesAsync(ct);
    }
}

/// <summary>Os tipos de aviso do ciclo de melhoria.</summary>
public static class AvisoDoCicloKinds
{
    /// <summary>O ciclo não é tocado há tempo demais.</summary>
    public const string CicloParado = "PDCA_CICLO_PARADO";
    /// <summary>O prazo da meta passou e o indicador não foi medido.</summary>
    public const string CheckVencido = "PDCA_CHECK_VENCIDO";
}
