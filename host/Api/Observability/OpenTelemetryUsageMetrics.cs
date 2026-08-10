using System.Diagnostics;
using System.Diagnostics.Metrics;
using TrinoSupply.BuildingBlocks.Abstractions;

namespace TrinoSupply.Api.Observability;

/// <summary>
/// Implementação de <see cref="IUsageMetrics"/> baseada em OpenTelemetry: cada ação de negócio vira
/// um incremento no contador <c>trino.business.actions</c> com a dimensão <c>action</c> (exposto em
/// <c>/metrics</c> no formato Prometheus). Sem o tenant como rótulo — evita explosão de cardinalidade.
/// Não muda os call-sites (OPS-001 §observabilidade).
/// </summary>
public sealed class OpenTelemetryUsageMetrics : IUsageMetrics
{
    public const string MeterName = "TrinoSupply.Business";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Actions = Meter.CreateCounter<long>(
        "trino.business.actions", unit: "1", description: "Ações de negócio, por tipo de ação.");

    public void Record(string action, string tenant, IReadOnlyDictionary<string, string>? tags = null)
    {
        var tagList = new TagList { { "action", action } };
        if (tags is not null)
            foreach (var (k, v) in tags)
                tagList.Add(k, v);
        Actions.Add(1, tagList);
    }
}
