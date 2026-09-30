using System.Security.Claims;
using TrinoSupply.Foundation.Api.Analytics;
using TrinoSupply.Foundation.Api.Domain;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// O cockpit: a leitura que vai para a TV da sala de Suprimentos.
///
/// <para>
/// É <b>uma chamada só</b>, de propósito. A tela gira sozinha e recarrega a cada poucos
/// segundos; se cada bloco buscasse o seu, um giro seriam seis requisições, e blocos de
/// instantes diferentes apareceriam lado a lado — o backlog de agora com o gargalo de
/// meio minuto atrás. Um payload com hora própria mantém a tela inteira dizendo a mesma
/// coisa sobre o mesmo momento.
/// </para>
/// </summary>
public static class CockpitRotas
{
    public static void MapCockpit(this WebApplication app)
    {
        app.MapGet("/api/v1/cockpit", async (CockpitService svc, ClaimsPrincipal p, HttpContext ctx,
            CancellationToken ct) =>
        {
            if (!CockpitService.CanView(RoleOf(p)))
                return Error(ctx, 403, "CKP-ERR-900", "Seu papel não acessa o cockpit.");
            if (!ModulesOf(p).Contains(AppModules.Compras))
                return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para este módulo.");

            return Ok(await svc.LerAsync(ct), ctx);
        }).RequireAuthorization();
    }
}
