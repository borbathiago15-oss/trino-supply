namespace TrinoSupply.BuildingBlocks.Abstractions;

/// <summary>
/// Registro de sinais de uso do produto por tenant. Base do "case de sucesso" (uso interno do
/// grupo Trino → futura oferta de plataforma): mede adoção e volume de operações de negócio.
/// Implementação inicial faz log estruturado; evolui para métricas/telemetria (OPS-001 §obs).
/// </summary>
public interface IUsageMetrics
{
    /// <summary>
    /// Registra a ocorrência de uma ação de negócio (ex.: <c>user.registered</c>, <c>purchase.approved</c>).
    /// <paramref name="tenant"/> é o CompanyId em texto; <paramref name="tags"/> são dimensões opcionais.
    /// </summary>
    void Record(string action, string tenant, IReadOnlyDictionary<string, string>? tags = null);
}
