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
        // ---- tipos de solicitação ----------------------------------------------------
        // O cadastro mora aqui, e não num arquivo de cadastros genéricos, porque a razão
        // de o tipo existir é o prazo: é ele que escolhe qual conjunto de SLA vale.
        var tipos = app.MapGroup("/api/v1/request-types").RequireAuthorization();
        tipos.AddEndpointFilter(RejectSupplierRole());

        static object TipoView(RequestType t) => new
        {
            id = t.Id, code = t.Code, name = t.Name, description = t.Description,
            active = t.Active, createdAt = t.CreatedAt, updatedAt = t.UpdatedAt,
        };

        tipos.MapGet("/", async (TipoDeSolicitacaoService svc, ClaimsPrincipal p, HttpContext ctx, bool? all) =>
        {
            // a lista é aberta a qualquer autenticado interno: quem abre uma SC precisa
            // escolher o tipo, e uma lista de tipos não vale um 403 a mais para administrar
            var mantem = TipoDeSolicitacaoService.CanMaintain(RoleOf(p));
            return Ok(new
            {
                canMaintain = mantem,
                items = (await svc.ListarAsync(all == true && mantem)).Select(TipoView),
            }, ctx);
        });

        tipos.MapPost("/", async (TipoDeSolicitacaoRequest body, TipoDeSolicitacaoService svc,
            ClaimsPrincipal p, HttpContext ctx) =>
        {
            var actor = BuildActor(p);
            if (actor is null) return Error(ctx, 403, "TP-ERR-900", "Seu papel não mantém os tipos de solicitação.");
            var (tipo, error) = await svc.CriarAsync(actor, body.Code, body.Name, body.Description);
            return error is not null
                ? Error(ctx, error.Code == "TP-ERR-900" ? 403 : error.Code == "TP-ERR-011" ? 409 : 400,
                    error.Code, error.Message)
                : Results.Json(new { data = TipoView(tipo!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        tipos.MapPatch("/{id:guid}", async (Guid id, AtualizaTipoDeSolicitacaoRequest body,
            TipoDeSolicitacaoService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var actor = BuildActor(p);
            if (actor is null) return Error(ctx, 403, "TP-ERR-900", "Seu papel não mantém os tipos de solicitação.");
            var (tipo, error) = await svc.AtualizarAsync(actor, id, body.Name, body.Description, body.Active);
            return error is not null
                ? Error(ctx, error.Code == "TP-ERR-900" ? 403 : error.Code == "TP-ERR-404" ? 404 : 400,
                    error.Code, error.Message)
                : Ok(TipoView(tipo!), ctx);
        });

        // ---- prazos por etapa ---------------------------------------------------
        var prazos = app.MapGroup("/api/v1/stage-sla").RequireAuthorization();
        prazos.AddEndpointFilter(RejectSupplierRole());

        prazos.MapGet("/", async (PrazoDaEtapaService svc, TipoDeSolicitacaoService tiposSvc,
            ClaimsPrincipal p, HttpContext ctx, string? requestType) =>
        {
            var atuais = await svc.AtuaisAsync(requestType);
            // quais etapas este tipo definiu por conta própria: sem isso a tela não
            // consegue dizer o que é dele e o que veio herdado do padrão
            var proprios = requestType is { Length: > 0 }
                ? (await svc.PropriosAsync(requestType)).ToHashSet()
                : [];
            return Ok(new
            {
                requestType = TipoDeSolicitacaoService.Normalizar(requestType) is { Length: > 0 } t ? t : null,
                types = (await tiposSvc.ListarAsync()).Select(x => new { code = x.Code, name = x.Name }),
                // a fração de atenção vem junto: a tela pinta de amarelo antes do estouro e
                // precisa dizer a partir de quando, em vez de guardar a régua para si
                warnAtPercent = Math.Round(PrazoDaEtapaService.FracaoDeAtencao * 100, 0),
                canEdit = PrazoDaEtapaService.CanEdit(RoleOf(p)),
                items = atuais.Select(s => new
                {
                    stage = s.Stage,
                    label = TorreDeControleService.RotuloDaEtapa(s.Stage),
                    maxDays = s.MaxDays,
                    // herdado = o tipo não definiu esta etapa e segue o padrão
                    inherited = requestType is { Length: > 0 } && !proprios.Contains(s.Stage),
                    updatedAt = s.UpdatedAt, updatedByLabel = s.UpdatedByLabel,
                }),
            }, ctx);
        });

        prazos.MapPut("/", async (PrazosDasEtapasRequest body, PrazoDaEtapaService svc,
            ClaimsPrincipal p, HttpContext ctx) =>
        {
            var actor = BuildActor(p);
            if (actor is null) return Error(ctx, 403, "SLA-ERR-900", "Seu papel não define os prazos das etapas.");
            var (salvos, error) = await svc.SalvarAsync(
                actor, body.Days ?? new Dictionary<string, int>(), body.RequestType, body.Inherit);
            return error is not null
                ? Error(ctx, error.Code == "SLA-ERR-900" ? 403 : 400, error.Code, error.Message)
                : Ok(new { items = salvos!.Select(s => new { stage = s.Stage, maxDays = s.MaxDays }) }, ctx);
        });
    }
}
