using System.Security.Claims;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Procurement;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// Torre de Controle: uma linha por item de compra, com etapa, situação e o
/// andamento de cada um — a tela que o comprador deixa aberta o dia inteiro.
/// </summary>
public static class TorreRotas
{
    public static void MapTorre(this WebApplication app)
    {
        app.MapGet("/api/v1/control-tower", async (TorreDeControleService svc,
            ClaimsPrincipal p, HttpContext ctx,
            string? search, string? stage, string? status, string? company, string? costCenter,
            string? family, Guid? requesterId, Guid? buyerId, string? priority,
            DateOnly? from, DateOnly? to, bool? late, int? page, int? pageSize,
            string? supplier, string? orderNumber, DateOnly? dueFrom, DateOnly? dueTo,
            decimal? minValue, decimal? maxValue, bool? exception,
            CancellationToken ct) =>
        {
            if (!TorreDeControleService.CanView(RoleOf(p)))
                return Error(ctx, 403, "TC-ERR-900", "Seu papel não acessa a Torre de Controle.");
            if (!ModulesOf(p).Contains(AppModules.Compras))
                return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para este módulo.");

            static string? Preenchido(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            var filtro = new FiltroTorre(
                Preenchido(search), Preenchido(stage), Preenchido(status), Preenchido(company),
                Preenchido(costCenter), Preenchido(family), requesterId, buyerId, Preenchido(priority),
                from, to, late, page ?? 1, pageSize ?? 50,
                Preenchido(supplier), Preenchido(orderNumber), dueFrom, dueTo, minValue, maxValue, exception);
            return Ok(await svc.ConsultarAsync(filtro, ct), ctx);
        }).RequireAuthorization().AddEndpointFilter(RejectSupplierRole())
            .AddEndpointFilter(RequireModules(AppModules.Compras));
    }
}
