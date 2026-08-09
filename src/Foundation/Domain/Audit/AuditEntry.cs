using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Foundation.Domain.Audit;

/// <summary>
/// Registro de auditoria — fato imutável de "quem fez o quê" (SEC-001, SEC-002 anti-repúdio).
/// Append-only: nunca é atualizado ou removido (reforçado por trigger + RLS + grants no banco).
/// Escopado ao tenant. Não é AggregateRoot: não emite eventos, é o próprio efeito colateral auditável.
/// </summary>
public sealed class AuditEntry : IBelongsToTenant
{
    private AuditEntry(
        Guid id, CompanyId companyId, DateTimeOffset occurredAt, string actorSubject,
        string action, string? targetType, string? targetId, string? metadata)
    {
        Id = id;
        CompanyId = companyId;
        OccurredAt = occurredAt;
        ActorSubject = actorSubject;
        Action = action;
        TargetType = targetType;
        TargetId = targetId;
        Metadata = metadata;
    }

    // Exigido pelo EF Core.
    private AuditEntry() { }

    public Guid Id { get; private set; }
    public CompanyId CompanyId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Sujeito que executou a ação (claim <c>sub</c>), ou <c>system</c> em operações de plataforma.</summary>
    public string ActorSubject { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string? TargetType { get; private set; }
    public string? TargetId { get; private set; }

    /// <summary>Dados adicionais em JSON (jsonb). Nunca inclui segredos/PII sensível — SEC-003.</summary>
    public string? Metadata { get; private set; }

    public static AuditEntry Create(
        CompanyId companyId, DateTimeOffset occurredAt, string actorSubject,
        string action, string? targetType = null, string? targetId = null, string? metadata = null) =>
        new(Guid.NewGuid(), companyId, occurredAt, actorSubject, action, targetType, targetId, metadata);
}
