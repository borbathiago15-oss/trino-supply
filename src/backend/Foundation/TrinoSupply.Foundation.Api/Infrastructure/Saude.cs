namespace TrinoSupply.Foundation.Api.Infrastructure;

/// <summary>
/// O corpo do <c>/health</c>. O healthcheck é anônimo e responde só o estado; commit e horário
/// de subida dizem a quem sonda qual versão está no ar e quando reiniciou (auditoria A8), e vão
/// só para quem chega com sessão — a verificação da entrega continua possível com o token.
/// </summary>
public static class Saude
{
    public static object Corpo(bool banco, bool autenticado, bool setupComplete, string? commit,
        DateTimeOffset iniciadoEm, DateTimeOffset agora)
    {
        var status = banco ? "healthy" : "degraded";
        var database = banco ? "up" : "down";
        if (!autenticado) return new { status, database };
        return new
        {
            status,
            service = "trino-supply-foundation",
            database,
            setupComplete,
            commit,
            commitShort = VersaoImplantada.Curto(commit),
            startedAt = iniciadoEm,
            timestamp = agora,
        };
    }
}
