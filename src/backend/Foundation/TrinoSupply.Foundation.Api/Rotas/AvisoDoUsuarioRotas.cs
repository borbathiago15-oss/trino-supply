using System.Security.Claims;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Procurement;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// A caixa de avisos de quem está logado.
///
/// <para>
/// <b>Só o dono lê e marca os seus.</b> Não há rota para ler a caixa de outra pessoa: um aviso
/// diz o que aconteceu com o trabalho de alguém, e quem precisa ver o todo tem a Torre.
/// </para>
/// </summary>
public static class AvisoDoUsuarioRotas
{
    public static void MapAvisosDoUsuario(this WebApplication app)
    {
        var avisos = app.MapGroup("/api/v1/notices").RequireAuthorization();
        avisos.AddEndpointFilter(RejectSupplierRole());

        avisos.MapGet("/", async (AvisoDoUsuarioService svc, EscalonamentoDoPrazoService escalonamento,
            ClaimsPrincipal p, HttpContext ctx, bool? unreadOnly) =>
        {
            var eu = ActorId(p);
            if (eu == Guid.Empty) return Error(ctx, 403, "AV-ERR-900", "Sessão sem usuário.");

            // o escalonamento é avaliado aqui, na leitura do gestor: o projeto não tem
            // agendador, e um relógio de servidor entregaria o mesmo recado com uma peça a
            // mais que pode falhar em silêncio. A deduplicação impede o aviso de renascer
            // a cada visita
            if (RoleOf(p) == Roles.SupplyManager) await escalonamento.AvaliarAsync();

            var itens = await svc.DaPessoaAsync(eu, unreadOnly == true);
            return Ok(new
            {
                unread = await svc.NaoLidosAsync(eu),
                items = itens.Select(n => new
                {
                    id = n.Id, kind = n.Kind, title = n.Title, body = n.Body,
                    link = n.Link, createdAt = n.CreatedAt, read = n.ReadAt is not null,
                }),
            }, ctx);
        });

        avisos.MapPost("/{id:guid}/read", async (Guid id, AvisoDoUsuarioService svc,
            ClaimsPrincipal p, HttpContext ctx) =>
        {
            var eu = ActorId(p);
            var ok = eu != Guid.Empty && await svc.MarcarLidoAsync(eu, id);
            // aviso de outra pessoa devolve 404, e não 403: dizer "existe, mas não é seu"
            // já contaria algo sobre o trabalho alheio
            return ok ? Ok(new { read = true }, ctx)
                : Error(ctx, 404, "AV-ERR-404", "Aviso não encontrado.");
        });

        avisos.MapPost("/read-all", async (AvisoDoUsuarioService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var eu = ActorId(p);
            if (eu == Guid.Empty) return Error(ctx, 403, "AV-ERR-900", "Sessão sem usuário.");
            return Ok(new { read = await svc.MarcarTudoLidoAsync(eu) }, ctx);
        });
    }
}
