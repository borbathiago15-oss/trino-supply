namespace TrinoSupply.Foundation.Application.Audit;

/// <summary>
/// Trilha de auditoria (SEC-001/SEC-002). Registra ações sensíveis no MESMO contexto/transação da
/// operação — a auditoria é gravada se, e somente se, a ação for confirmada (atomicidade).
/// Append-only: o registro é enfileirado no contexto e persistido junto no SaveChanges.
/// </summary>
public interface IAuditLog
{
    /// <summary>Registra uma ação do tenant corrente (ator = subject do JWT, ou <c>system</c>).</summary>
    void Record(string action, string? targetType = null, string? targetId = null, object? metadata = null);
}

/// <summary>Representação de leitura de um registro de auditoria.</summary>
public sealed record AuditView(
    Guid Id, DateTimeOffset OccurredAt, string Actor, string Action,
    string? TargetType, string? TargetId, string? Metadata);
