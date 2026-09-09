using System.Security.Claims;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Procurement;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// Os cadastros de pagamento: <b>forma</b> (por onde sai) e <b>condição</b> (quando se
/// paga). São master data do time de compras, no mesmo molde de centros de custo e
/// empresas — manter é de quem compra, ler é de quem preenche proposta.
///
/// <para>
/// A leitura é aberta a qualquer autenticado interno de propósito: quem registra a
/// proposta precisa da lista, e uma lista de formas de pagamento não é informação
/// que valha um 403 a mais para administrar.
/// </para>
/// </summary>
public static class PagamentoRotas
{
    public static void MapPagamentos(this WebApplication app)
    {
        static object FormaView(PaymentMethod m) => new
        {
            id = m.Id, name = m.Name, active = m.Active,
            createdAt = m.CreatedAt, updatedAt = m.UpdatedAt,
        };

        static object CondicaoView(PaymentTermOption c) => new
        {
            id = c.Id, name = c.Name, installments = c.Installments,
            firstDueDays = c.FirstDueDays, isDefault = c.IsDefault, active = c.Active,
            createdAt = c.CreatedAt, updatedAt = c.UpdatedAt,
        };

        // manter exige papel de compras E o módulo: o mesmo par que o resto dos cadastros
        static bool Mantem(ClaimsPrincipal p) =>
            PagamentoService.CanMaintain(RoleOf(p)) && ModulesOf(p).Contains(AppModules.Compras);

        static int Http(string codigo) => codigo.EndsWith("-404") ? 404
            : codigo is "PAY-ERR-011" or "PAY-ERR-023" ? 409 : 400;

        // ---- formas de pagamento ------------------------------------------------
        var formas = app.MapGroup("/api/v1/payment-methods").RequireAuthorization();
        formas.AddEndpointFilter(RejectSupplierRole());

        formas.MapGet("/", async (PagamentoService svc, ClaimsPrincipal p, HttpContext ctx, bool? all) =>
        {
            var inativas = all == true && Mantem(p);
            return Ok(new { items = (await svc.FormasAsync(inativas)).Select(FormaView) }, ctx);
        });

        formas.MapPost("/", async (CreatePaymentMethodRequest body, PagamentoService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!Mantem(p)) return Error(ctx, 403, "PAY-ERR-900", "Seu usuário não mantém as formas de pagamento.");
            var (forma, error) = await svc.CriarFormaAsync(body.Name);
            return error is not null ? Error(ctx, Http(error.Code), error.Code, error.Message)
                : Results.Json(new { data = FormaView(forma!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        formas.MapPatch("/{id:guid}", async (Guid id, UpdatePaymentMethodRequest body, PagamentoService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!Mantem(p)) return Error(ctx, 403, "PAY-ERR-900", "Seu usuário não mantém as formas de pagamento.");
            var (forma, error) = await svc.AtualizarFormaAsync(id, body.Name, body.Active);
            return error is not null ? Error(ctx, Http(error.Code), error.Code, error.Message)
                : Ok(FormaView(forma!), ctx);
        });

        // ---- condições de pagamento ---------------------------------------------
        var condicoes = app.MapGroup("/api/v1/payment-terms").RequireAuthorization();
        condicoes.AddEndpointFilter(RejectSupplierRole());

        condicoes.MapGet("/", async (PagamentoService svc, ClaimsPrincipal p, HttpContext ctx, bool? all) =>
        {
            var inativas = all == true && Mantem(p);
            return Ok(new { items = (await svc.CondicoesAsync(inativas)).Select(CondicaoView) }, ctx);
        });

        condicoes.MapPost("/", async (CreatePaymentTermRequest body, PagamentoService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!Mantem(p)) return Error(ctx, 403, "PAY-ERR-900", "Seu usuário não mantém as condições de pagamento.");
            var (condicao, error) = await svc.CriarCondicaoAsync(body.Name, body.Installments, body.FirstDueDays, body.IsDefault);
            return error is not null ? Error(ctx, Http(error.Code), error.Code, error.Message)
                : Results.Json(new { data = CondicaoView(condicao!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        condicoes.MapPatch("/{id:guid}", async (Guid id, UpdatePaymentTermRequest body, PagamentoService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!Mantem(p)) return Error(ctx, 403, "PAY-ERR-900", "Seu usuário não mantém as condições de pagamento.");
            var (condicao, error) = await svc.AtualizarCondicaoAsync(id, body.Name, body.Installments, body.FirstDueDays, body.IsDefault, body.Active);
            return error is not null ? Error(ctx, Http(error.Code), error.Code, error.Message)
                : Ok(CondicaoView(condicao!), ctx);
        });
    }
}
