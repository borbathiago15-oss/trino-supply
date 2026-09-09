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
            decimal? minValue, decimal? maxValue, int? agingBand, bool? exception, bool? needsBuyer,
            CancellationToken ct) =>
        {
            if (!TorreDeControleService.CanView(RoleOf(p)))
                return Error(ctx, 403, "TC-ERR-900", "Seu papel não acessa a Torre de Controle.");
            if (!ModulesOf(p).Contains(AppModules.Compras))
                return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para este módulo.");

            static string? Preenchido(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            // por nome, e não por posição: o filtro cresce a cada recorte novo, e um
            // parâmetro inserido no meio silenciosamente deslocava todos os seguintes
            var filtro = new FiltroTorre(
                Search: Preenchido(search), Stage: Preenchido(stage), Status: Preenchido(status),
                Company: Preenchido(company), CostCenter: Preenchido(costCenter), Family: Preenchido(family),
                RequesterId: requesterId, BuyerId: buyerId, Priority: Preenchido(priority),
                From: from, To: to, Late: late, Page: page ?? 1, PageSize: pageSize ?? 50,
                Supplier: Preenchido(supplier), OrderNumber: Preenchido(orderNumber),
                DueFrom: dueFrom, DueTo: dueTo, MinValue: minValue, MaxValue: maxValue,
                AgingBand: agingBand, Exception: exception, NeedsBuyer: needsBuyer);
            return Ok(await svc.ConsultarAsync(filtro, ct), ctx);
        }).RequireAuthorization().AddEndpointFilter(RejectSupplierRole())
            .AddEndpointFilter(RequireModules(AppModules.Compras));
    }
}
