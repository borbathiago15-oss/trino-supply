using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Acoes;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Melhoria;

/// <param name="Code">O código do achado (INS-01, INS-05…).</param>
/// <param name="Title">O título do achado, que vira o problema do ciclo.</param>
/// <param name="Evidence">A evidência que o sustenta — a situação atual do ciclo.</param>
/// <param name="Action">A providência que o achado já sugere.</param>
/// <param name="CriarPlano">Abre junto o plano de contramedida.</param>
public record AchadoATratar(
    string Code, string Title, string? Evidence, string? Action,
    string? CostCenter = null, bool CriarPlano = false);

public record CausaAberta(ImprovementCycle Ciclo, ActionPlan? Plano, bool JaExistia);

/// <summary>
/// O caminho que faltava: <b>achado → causa → contramedida</b>.
///
/// <para>
/// O Insights já aponta o problema com a evidência junto, e até hoje parava aí — quem lia
/// tinha de abrir um ciclo na mão e redigitar o que a tela já dizia. Agora o achado abre o
/// ciclo com o problema, a situação e os 5 Porquês começados, e opcionalmente o plano da
/// contramedida. É o caminho do Trino Intelligence (indicador crítico → análise de causa →
/// plano), com os nomes daqui.
/// </para>
///
/// <para>
/// <b>Clicar duas vezes não abre dois ciclos.</b> O mesmo achado tem a mesma chave de origem,
/// e a segunda chamada devolve o ciclo que já existe — senão a lista encheria de ciclos
/// gêmeos, cada um com um pedaço da análise.
/// </para>
/// </summary>
public class CausaDoAchadoService(AppDbContext db, TimeProvider clock)
{
    /// <summary>
    /// A chave de origem de um achado. Ela junta o código com o assunto normalizado: o mesmo
    /// achado sobre o mesmo item é o mesmo problema, mesmo que o texto mude de um mês para o
    /// outro.
    /// </summary>
    public static string ChaveDoAchado(string codigo, string titulo)
    {
        var assunto = MotorDeLeitura.Chave(titulo ?? "");
        if (assunto.Length > 150) assunto = assunto[..150];
        return $"INSIGHT:{(codigo ?? "").Trim().ToUpperInvariant()}:{assunto}";
    }

    public async Task<(CausaAberta? aberta, UserError? erro)> AbrirAsync(
        Actor ator, AchadoATratar achado, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(achado.Code) || string.IsNullOrWhiteSpace(achado.Title))
            return (null, new("PDCA-ERR-017", "Achado sem código ou sem título não vira ciclo."));

        var chave = ChaveDoAchado(achado.Code, achado.Title);
        var existente = await db.ImprovementCycles.Include(c => c.Tools)
            .FirstOrDefaultAsync(c => c.OriginKey == chave, ct);
        if (existente is not null)
        {
            var plano = await db.ActionPlans.FirstOrDefaultAsync(p => p.CycleId == existente.Id, ct);
            return (new(existente, plano, true), null);
        }

        var agora = clock.GetUtcNow();
        var titulo = Titulo(achado.Title);
        var ciclo = new ImprovementCycle
        {
            Code = await ProximoCodigoAsync(agora, ct),
            Title = titulo,
            Scope = achado.CostCenter is null ? EscopoDoCiclo.Gestao : EscopoDoCiclo.Centro,
            Phase = FaseDoCiclo.Plan,
            Problem = achado.Title.Trim(),
            CurrentSituation = Limpo(achado.Evidence),
            OriginKey = chave,
            OriginLabel = $"Achado {achado.Code.Trim().ToUpperInvariant()} do Insights",
            OwnerId = ator.Id, OwnerLabel = ator.Label,
            StartDate = DateOnly.FromDateTime(agora.UtcDateTime),
            CreatedBy = ator.Id, CreatedByLabel = ator.Label,
            CreatedAt = agora, UpdatedAt = agora,
        };
        if (achado.CostCenter is { } cc && !string.IsNullOrWhiteSpace(cc))
            ciclo.CostCenters.Add(new() { CycleId = ciclo.Id, CostCenter = cc.Trim().ToUpperInvariant() });

        // os 5 Porquês já começam com o problema escrito: o primeiro "por quê?" é a única
        // pergunta que o sistema pode fazer sozinho, e deixar a folha em branco devolveria
        // para quem lê o trabalho que a tela já tinha feito
        ciclo.Tools.Add(new CycleTool
        {
            CycleId = ciclo.Id,
            ToolType = FerramentaDeCausa.CincoPorques,
            ToolData = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["problema"] = achado.Title.Trim(),
                ["porque1"] = "Por que isto acontece?",
            }),
            Seq = 1,
            UpdatedAt = agora,
        });

        db.ImprovementCycles.Add(ciclo);

        ActionPlan? novoPlano = null;
        if (achado.CriarPlano)
        {
            novoPlano = new ActionPlan
            {
                Code = await ProximoCodigoDoPlanoAsync(agora, ct),
                Title = $"Contramedida: {titulo}",
                Problem = achado.Title.Trim(),
                BusinessReason = Limpo(achado.Action) ?? Limpo(achado.Evidence),
                Description = Limpo(achado.Evidence),
                CostCenter = Limpo(achado.CostCenter)?.ToUpperInvariant(),
                Priority = "ALTA",
                CycleId = ciclo.Id,
                StartDate = DateOnly.FromDateTime(agora.UtcDateTime),
                CreatedBy = ator.Id, CreatedByLabel = ator.Label,
                CreatedAt = agora, UpdatedAt = agora,
            };
            db.ActionPlans.Add(novoPlano);
            db.ActionPlanResponsibles.Add(new()
            {
                PlanId = novoPlano.Id, UserId = ator.Id, UserLabel = ator.Label,
            });
        }

        // um SaveChanges só: ciclo sem o plano que ele prometeu seria pior que nenhum dos dois
        await db.SaveChangesAsync(ct);
        return (new(ciclo, novoPlano, false), null);
    }

    /// <summary>
    /// O título do ciclo é uma <b>frase</b>, e o achado costuma vir com uma. Curto demais
    /// ganha um verbo na frente, para o ciclo não nascer com um rótulo por nome.
    /// </summary>
    private static string Titulo(string titulo)
    {
        var t = titulo.Trim();
        return t.Length >= 10 ? t : $"Tratar {t}";
    }

    private async Task<string> ProximoCodigoAsync(DateTimeOffset agora, CancellationToken ct)
    {
        var prefixo = $"PDCA-{agora.Year}-";
        var usados = await db.ImprovementCycles.Where(c => c.Code.StartsWith(prefixo))
            .Select(c => c.Code).ToListAsync(ct);
        var maior = usados.Select(n => int.TryParse(n[prefixo.Length..], out var v) ? v : 0)
            .DefaultIfEmpty(0).Max();
        return $"{prefixo}{maior + 1:000}";
    }

    private async Task<string> ProximoCodigoDoPlanoAsync(DateTimeOffset agora, CancellationToken ct)
    {
        var prefixo = $"AP-{agora.Year}-";
        var usados = await db.ActionPlans.Where(p => p.Code.StartsWith(prefixo))
            .Select(p => p.Code).ToListAsync(ct);
        var maior = usados.Select(n => int.TryParse(n[prefixo.Length..], out var v) ? v : 0)
            .DefaultIfEmpty(0).Max();
        return $"{prefixo}{maior + 1:000}";
    }

    private static string? Limpo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
