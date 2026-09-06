using System.Security.Claims;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Inventory;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// O que toda rota usa: o envelope da resposta, a leitura do token e os filtros
/// de acesso dos grupos.
///
/// Eram funções locais do Program.cs, alcançáveis só de lá. Passaram para cá
/// para que as rotas possam sair daquele arquivo — os arquivos de rota (e o
/// próprio Program.cs) chamam com `using static`, então o nome no ponto de
/// chamada continua o mesmo (ARQ-A).
/// </summary>
public static class Api
{
    // ---- envelope da resposta ------------------------------------------------

    public static IResult Ok(object data, HttpContext ctx) =>
        Results.Json(new { data, correlationId = CorrelationId(ctx) });

    public static IResult Error(HttpContext ctx, int status, string code, string message) =>
        Results.Json(new { error = new { code, message, correlationId = CorrelationId(ctx) } }, statusCode: status);

    public static string CorrelationId(HttpContext ctx) =>
        ctx.Request.Headers.TryGetValue("X-Correlation-Id", out var v) && !string.IsNullOrWhiteSpace(v)
            ? v.ToString()
            : ctx.TraceIdentifier;

    // ---- quem está pedindo ---------------------------------------------------

    public static string RoleOf(ClaimsPrincipal p) => p.FindFirstValue(ClaimTypes.Role) ?? "";

    public static string[] ModulesOf(ClaimsPrincipal p)
    {
        var claim = p.FindFirst("modules");
        if (claim is null) // token antigo sem o claim: cai no padrão do papel
            return AppModules.DefaultsFor(p.FindFirstValue(ClaimTypes.Role) ?? "");
        return claim.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static Guid ActorId(ClaimsPrincipal p) =>
        Guid.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue("sub"), out var id)
            ? id : Guid.Empty;

    public static Actor? BuildActor(ClaimsPrincipal p)
    {
        var id = ActorId(p);
        if (id == Guid.Empty) return null;
        var actor = new Actor(id,
            p.FindFirstValue("name") ?? p.FindFirstValue(ClaimTypes.Email) ?? "Usuário",
            p.FindFirstValue(ClaimTypes.Role) ?? "");
        return actor.CanAccessModule ? actor : null;
    }

    public static bool CanOperateStock(ClaimsPrincipal p) =>
        InventoryService.CanOperate(RoleOf(p)) || ModulesOf(p).Contains(AppModules.Estoque);

    public static bool CanViewStock(ClaimsPrincipal p) =>
        InventoryService.CanView(RoleOf(p)) || ModulesOf(p).Contains(AppModules.Estoque);

    // ---- filtros de grupo ----------------------------------------------------

    /// <summary>O token do Portal do Fornecedor nunca acessa módulo interno (RFQ-001 §5).</summary>
    public static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> RejectSupplierRole() =>
        async (ic, next) =>
        {
            if (ic.HttpContext.User.FindFirstValue(ClaimTypes.Role) == "Supplier")
                return Error(ic.HttpContext, 403, "RFQ-ERR-050", "Acesso restrito ao Portal do Fornecedor.");
            return await next(ic);
        };

    public static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> RequireModules(
        params string[] modules) =>
        async (ic, next) =>
        {
            var granted = ModulesOf(ic.HttpContext.User);
            if (modules.Any(granted.Contains)) return await next(ic);
            return Error(ic.HttpContext, 403, "IAM-ERR-018",
                "Seu usuário não tem autorização para este módulo. Fale com o administrador.");
        };

    // ---- rótulos que a API publica -------------------------------------------

    public static string QKindLabel(QuotationKind k) => k switch
    {
        QuotationKind.Bid => "BID", QuotationKind.Service => "SERVICO", _ => "COMPRA",
    };

    public static string QStatusLabel(QuotationStatus s) => s switch
    {
        QuotationStatus.Open => "COTACAO_ABERTA",
        QuotationStatus.Analysis => "EM_ANALISE",
        QuotationStatus.AwaitingManager => "AGUARDANDO_GERENTE",
        QuotationStatus.AwaitingDirector => "AGUARDANDO_DIRETOR",
        QuotationStatus.ApprovedForIssue => "APROVADO_PARA_EMISSAO",
        QuotationStatus.PoIssued => "OC_REGISTRADA",
        QuotationStatus.Rejected => "REJEITADO",
        _ => "CANCELADA",
    };
}
