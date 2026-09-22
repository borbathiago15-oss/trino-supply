using System.Security.Claims;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Users;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// Cadastro de setores — a dimensão "quem trabalha", ao lado do centro de custo, que é
/// "onde o dinheiro cai". A listagem é aberta a autenticados porque os formulários precisam
/// do picker; manter é de quem mantém dimensão organizacional.
/// </summary>
public static class SetorRotas
{
    public static void MapSetores(this WebApplication app)
    {
        var setores = app.MapGroup("/api/v1/sectors").RequireAuthorization();
        setores.AddEndpointFilter(RejectSupplierRole());

        static object Vista(Sector s) => new
        {
            id = s.Id, code = s.Code, name = s.Name, active = s.Active,
        };

        setores.MapGet("/", async (SectorService svc, ClaimsPrincipal p, HttpContext ctx, bool? all,
            CancellationToken ct) =>
        {
            var incluirInativos = all == true && SectorService.CanMaintain(RoleOf(p));
            return Ok(new { items = (await svc.ListAsync(incluirInativos, ct)).Select(Vista) }, ctx);
        });

        setores.MapPost("/", async (SetorRequest body, SectorService svc, ClaimsPrincipal p,
            HttpContext ctx, CancellationToken ct) =>
        {
            if (!SectorService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.CentrosCusto))
                return Error(ctx, 403, "SET-ERR-900", "Seu usuário não mantém setores.");
            var (setor, erro) = await svc.CreateAsync(ActorId(p), body.Code, body.Name, ct);
            return erro is not null ? Error(ctx, 400, erro.Code, erro.Message)
                : Results.Json(new { data = Vista(setor!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        setores.MapPatch("/{id:guid}", async (Guid id, AtualizarSetorRequest body, SectorService svc,
            ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            if (!SectorService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.CentrosCusto))
                return Error(ctx, 403, "SET-ERR-900", "Seu usuário não mantém setores.");
            var (setor, erro) = await svc.UpdateAsync(id, body.Name, body.Active, ct);
            return erro is not null ? Error(ctx, erro.Code == "SET-ERR-404" ? 404 : 400, erro.Code, erro.Message)
                : Ok(Vista(setor!), ctx);
        });
    }
}
