namespace TrinoSupply.Api.Observability;

/// <summary>
/// Correlação de request (OPS-001 §5 / observabilidade): usa o <c>X-Correlation-ID</c> recebido (ex.:
/// propagado pela Cloudflare / gateway) ou gera um novo, ecoa no cabeçalho da resposta e abre um
/// <b>escopo de log</b> com o id — assim toda linha de log do request sai correlacionada nos logs JSON.
/// </summary>
public sealed class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 64)
            correlationId = Guid.NewGuid().ToString("n");

        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }
}
