using Microsoft.Extensions.Logging;
using TrinoSupply.BuildingBlocks.Abstractions;

namespace TrinoSupply.Foundation.Infrastructure.Observability;

/// <summary>
/// Implementação inicial de <see cref="IUsageMetrics"/>: log estruturado por ação/tenant.
/// Alimenta o "case de sucesso" (uso interno do grupo Trino). Evolui para métricas/telemetria
/// exportáveis (OPS-001 §observabilidade) sem mudar os call-sites.
/// </summary>
public sealed class LoggingUsageMetrics(ILogger<LoggingUsageMetrics> logger) : IUsageMetrics
{
    public void Record(string action, string tenant, IReadOnlyDictionary<string, string>? tags = null)
    {
        logger.LogInformation("usage {Action} tenant={Tenant} tags={Tags}",
            action, tenant, tags ?? new Dictionary<string, string>());
    }
}
