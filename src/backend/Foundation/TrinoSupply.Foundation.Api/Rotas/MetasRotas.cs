using System.Security.Claims;
using TrinoSupply.Foundation.Api.Analytics;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// As metas dos indicadores do painel e da diretoria. Como a régua do score: <b>ler é de todo
/// mundo, alterar é do administrador</b> — quem vê "85% da meta" precisa poder saber que meta é
/// essa, e mudar a meta muda a cor de todos os cards da empresa de uma vez.
/// </summary>
public static class MetasRotas
{
    public record MetasRequest(IReadOnlyList<MetaInput> Goals);

    public static void MapMetas(this WebApplication app)
    {
        var metas = app.MapGroup("/api/v1/indicator-goals").RequireAuthorization();
        metas.AddEndpointFilter(RejectSupplierRole());

        static object View(IReadOnlyDictionary<string, IndicatorGoal> gravadas) =>
            MetasDosIndicadores.Catalogo.Select(i =>
            {
                gravadas.TryGetValue(i.Codigo, out var g);
                return new
                {
                    indicator = i.Codigo, label = i.Rotulo, unit = i.Unidade,
                    higherIsBetter = i.MaiorEMelhor, accumulates = i.Acumula,
                    monthlyValue = g?.MonthlyValue, updatedAt = g?.UpdatedAt, updatedByLabel = g?.UpdatedByLabel,
                };
            });

        metas.MapGet("/", async (MetasDosIndicadoresService svc, ClaimsPrincipal p, HttpContext ctx) =>
            Ok(new { goals = View(await svc.AtuaisAsync()), canEdit = MetasDosIndicadoresService.CanEdit(RoleOf(p)) }, ctx));

        metas.MapPut("/", async (MetasRequest body, MetasDosIndicadoresService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var actor = BuildActor(p);
            if (actor is null) return Error(ctx, 403, "MET-ERR-900", "Só o administrador define as metas dos indicadores.");
            var (salvas, erro) = await svc.SalvarAsync(actor, body.Goals ?? []);
            return erro is not null
                ? Error(ctx, erro.Code == "MET-ERR-900" ? 403 : 400, erro.Code, erro.Message)
                : Ok(new { goals = View(salvas!), canEdit = true }, ctx);
        });
    }
}
