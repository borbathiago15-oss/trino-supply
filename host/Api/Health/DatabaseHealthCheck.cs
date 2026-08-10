using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace TrinoSupply.Api.Health;

/// <summary>
/// Readiness do banco (OPS-001 §5): abre uma conexão e roda <c>SELECT 1</c>. Marcado com a tag
/// <c>ready</c> — só entra na sonda de <c>/health/ready</c>, nunca na de liveness (para não reiniciar
/// o processo por indisponibilidade transitória do Postgres).
/// </summary>
public sealed class DatabaseHealthCheck(IConfiguration config) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var cs = config.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(cs))
            return HealthCheckResult.Unhealthy("Sem connection string 'Postgres'.");

        try
        {
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync(ct);
            return HealthCheckResult.Healthy("Postgres acessível.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Postgres inacessível.", ex);
        }
    }
}

/// <summary>Serializa o resultado das sondas em JSON (status geral + por checagem), sem vazar exceções.</summary>
public static class HealthJson
{
    public static Task WriteAsync(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
            }),
        };
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
