namespace TrinoSupply.BuildingBlocks.Multitenancy;

/// <summary>
/// Identidade da empresa (tenant). Isolamento multi-tenant obrigatório em toda operação
/// (ARC-006 §1, SEC-001). Complementado por RLS no PostgreSQL (ADR-015 §3).
/// </summary>
public readonly record struct CompanyId(Guid Value)
{
    public static CompanyId New() => new(Guid.NewGuid());

    public static CompanyId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("CompanyId não pode ser vazio.", nameof(value))
        : new CompanyId(value);

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Contexto do tenant resolvido a partir do JWT/escopo da requisição (FD-001-01).
/// Toda leitura/escrita filtra por <see cref="CompanyId"/>; recurso fora do escopo → 404 (anti-enumeração).
/// </summary>
public interface ITenantContext
{
    CompanyId CompanyId { get; }
    bool HasTenant { get; }
}

/// <summary>
/// Agregado que pertence a um tenant. Permite ao Outbox resolver o <c>company_id</c> do evento
/// a partir do próprio agregado no bootstrap (quando ainda não há tenant na requisição) — SEC-004 §7.
/// </summary>
public interface IBelongsToTenant
{
    CompanyId CompanyId { get; }
}
