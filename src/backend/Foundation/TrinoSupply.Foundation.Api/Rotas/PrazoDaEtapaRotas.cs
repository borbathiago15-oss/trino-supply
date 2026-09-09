using System.Security.Claims;
using TrinoSupply.Foundation.Api.Procurement;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// Os prazos por etapa: quanto tempo cada uma pode levar antes de a Torre marcar o estouro.
///
/// <para>
/// <b>Ler é de quem usa a Torre, alterar é do administrador.</b> Quem vê a linha vermelha
/// precisa saber contra que prazo ela ficou vermelha; mudar o prazo muda o veredito de toda
/// a operação de uma vez.
/// </para>
/// </summary>
public static class PrazoDaEtapaRotas
{
    public static void MapPrazosDasEtapas(this WebApplication app)
    {
        var prazos = app.MapGroup("/api/v1/stage-sla").RequireAuthorization();
        prazos.AddEndpointFilter(RejectSupplierRole());

        prazos.MapGet("/", async (PrazoDaEtapaService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var atuais = await svc.AtuaisAsync();
            return Ok(new
            {
                // a fração de atenção vem junto: a tela pinta de amarelo antes do estouro e
                // precisa dizer a partir de quando, em vez de guardar a régua para si
                warnAtPercent = Math.Round(PrazoDaEtapaService.FracaoDeAtencao * 100, 0),
                canEdit = PrazoDaEtapaService.CanEdit(RoleOf(p)),
                items = atuais.Select(s => new
                {
                    stage = s.Stage,
                    label = TorreDeControleService.RotuloDaEtapa(s.Stage),
                    maxDays = s.MaxDays,
                    updatedAt = s.UpdatedAt, updatedByLabel = s.UpdatedByLabel,
                }),
            }, ctx);
        });

        prazos.MapPut("/", async (PrazosDasEtapasRequest body, PrazoDaEtapaService svc,
            ClaimsPrincipal p, HttpContext ctx) =>
        {
            var actor = BuildActor(p);
            if (actor is null) return Error(ctx, 403, "SLA-ERR-900", "Seu papel não define os prazos das etapas.");
            var (salvos, error) = await svc.SalvarAsync(actor, body.Days ?? new Dictionary<string, int>());
            return error is not null
                ? Error(ctx, error.Code == "SLA-ERR-900" ? 403 : 400, error.Code, error.Message)
                : Ok(new { items = salvos!.Select(s => new { stage = s.Stage, maxDays = s.MaxDays }) }, ctx);
        });
    }
}
