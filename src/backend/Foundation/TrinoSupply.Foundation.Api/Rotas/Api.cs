using System.Security.Claims;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Metadata;
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

    /// <summary>Código da falha que ninguém previu — a única que não tem regra de negócio atrás.</summary>
    public const string CodigoDeFalhaInesperada = "SYS-ERR-500";

    /// <summary>
    /// Falha não prevista responde no mesmo envelope do resto da API e deixa rastro.
    ///
    /// Sem isto o 500 sai com corpo vazio: o usuário vê "o servidor não respondeu" e não
    /// tem número nenhum para relatar, e quem for investigar não consegue ligar a queixa
    /// dele à linha do log. A correlação passa a aparecer nos dois lados — na mensagem e
    /// no registro —, que é o que torna o suporte possível.
    ///
    /// Vale também em Development, de propósito: o comportamento que só existe em
    /// produção é o que ninguém testa. A exceção inteira continua indo para o log.
    /// </summary>
    public static void UsarEnvelopeDeFalha(this IApplicationBuilder app) =>
        app.UseExceptionHandler(ramo => ramo.Run(async ctx =>
        {
            var falha = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
            var correlacao = CorrelationId(ctx);
            ctx.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("TrinoSupply.Falha")
                .LogError(falha, "Falha não tratada em {Metodo} {Caminho} (correlação {Correlacao})",
                    ctx.Request.Method, ctx.Request.Path.Value, correlacao);

            await Results.Json(new
            {
                error = new
                {
                    code = CodigoDeFalhaInesperada,
                    message = $"Falha inesperada no servidor. Informe o código {correlacao} ao suporte.",
                    correlationId = correlacao,
                },
            }, statusCode: 500).ExecuteAsync(ctx);
        }));

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

    /// <summary>O fornecedor por trás do token do portal, quando é um deles.</summary>
    public static Guid? PortalSupplierId(ClaimsPrincipal p) =>
        p.FindFirstValue(ClaimTypes.Role) == "Supplier" && Guid.TryParse(p.FindFirstValue("supplierId"), out var id)
            ? id : null;

    public static bool CanOperateStock(ClaimsPrincipal p) =>
        InventoryService.CanOperate(RoleOf(p)) || ModulesOf(p).Contains(AppModules.Estoque);

    public static bool CanViewStock(ClaimsPrincipal p) =>
        InventoryService.CanView(RoleOf(p)) || ModulesOf(p).Contains(AppModules.Estoque);

    /// <summary>
    /// De quem é a cota de uso: do usuário, e não do IP — um escritório inteiro
    /// atrás do mesmo IP não pode dividir o mesmo balde. Sem sessão, cai no IP.
    /// </summary>
    public static string QuemEsta(HttpContext ctx) =>
        ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? ctx.User.FindFirst("sub")?.Value
            ?? ctx.Connection.RemoteIpAddress?.ToString()
            ?? "desconhecido";

    /// <summary>
    /// Teto da requisição, declarado no endpoint (SEC-C). Minimal API não tem um
    /// `WithRequestSizeLimit`, mas honra este metadado: o servidor corta o envio
    /// grande demais em vez de lê-lo inteiro para a aplicação recusar depois.
    /// </summary>
    private sealed record TetoDeRequisicao(long? MaxRequestBodySize) : IRequestSizeLimitMetadata;

    public static TBuilder ComTetoDeUpload<TBuilder>(this TBuilder rota, long bytes)
        where TBuilder : IEndpointConventionBuilder
    {
        rota.Add(e => e.Metadata.Add(new TetoDeRequisicao(bytes)));
        return rota;
    }

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

    /// <summary>
    /// A situação pedida no filtro da lista. A tela manda a chave que ela mesma recebe
    /// (<c>EM_ANALISE</c>), e o filtro só aceitava o nome do enum (<c>Analysis</c>): a conversão
    /// falhava calada e a lista vinha inteira, com o filtro marcado na tela. As duas formas valem.
    /// </summary>
    public static QuotationStatus? StatusDoFiltro(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var chave = status.Trim().ToUpperInvariant();
        foreach (var s in Enum.GetValues<QuotationStatus>())
            if (QStatusLabel(s) == chave) return s;
        return Enum.TryParse<QuotationStatus>(status, true, out var st) ? st : null;
    }

    public static string QStatusLabel(QuotationStatus s) => s switch
    {
        QuotationStatus.Open => "COTACAO_ABERTA",
        QuotationStatus.Analysis => "EM_ANALISE",
        QuotationStatus.AwaitingManager => "AGUARDANDO_GERENTE",
        QuotationStatus.AwaitingDirector => "AGUARDANDO_DIRETOR",
        QuotationStatus.ApprovedForIssue => "APROVADO_PARA_EMISSAO",
        QuotationStatus.PoIssued => "OC_REGISTRADA",
        QuotationStatus.Rejected => "REJEITADO",
        QuotationStatus.BudgetPresented => "ORCAMENTO_APRESENTADO",
        _ => "CANCELADA",
    };
}
