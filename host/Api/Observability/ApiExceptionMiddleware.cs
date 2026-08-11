namespace TrinoSupply.Api.Observability;

/// <summary>
/// Tratamento global de exceções (OPS-001): nenhuma exceção não tratada chega crua ao cliente.
/// O detalhe técnico vai para o log (com CorrelationId, via escopo do <see cref="CorrelationMiddleware"/>);
/// o cliente recebe um JSON estável e amigável com o correlationId para suporte.
/// </summary>
public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Cliente desistiu da request — não é erro do servidor; não loga como falha.
        }
        catch (Exception ex)
        {
            var correlationId = context.Response.Headers[CorrelationMiddleware.HeaderName].ToString();
            logger.LogError(ex, "Exceção não tratada em {Method} {Path}", context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted) throw; // tarde demais para trocar a resposta

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.Headers[CorrelationMiddleware.HeaderName] = correlationId;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "internal.error",
                message = "Erro interno. Tente novamente; se persistir, informe o código ao suporte.",
                correlationId,
            });
        }
    }
}
