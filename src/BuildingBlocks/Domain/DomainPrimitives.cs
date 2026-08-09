namespace TrinoSupply.BuildingBlocks.Domain;

/// <summary>Fato de negócio no passado (ADR-010). Nome sempre de negócio (ex.: ItemActivated).</summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Contrato não-genérico das raízes de agregado que acumulam eventos de domínio. Permite ao
/// <c>DbContext</c> coletar eventos de QUALQUER agregado (independente do tipo do Id) para o Outbox
/// (ARC-005 §3). Sem isto, a varredura ficaria presa a um <c>AggregateRoot&lt;TId&gt;</c> específico.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}

/// <summary>Base de entidade com identidade e concorrência otimista (ARC-004 §3, ARC-006 §12.1).</summary>
public abstract class Entity<TId>
    where TId : notnull
{
    protected Entity(TId id) => Id = id;

    public TId Id { get; protected init; }

    /// <summary>Versão para optimistic concurrency (If-Match / rowversion).</summary>
    public int Version { get; protected set; } = 1;
}

/// <summary>
/// Raiz de agregado: única porta de mutação; acumula eventos de domínio publicados via Outbox
/// na mesma transação (ARC-004 §3/§4, ARC-005 §3).
/// </summary>
public abstract class AggregateRoot<TId>(TId id) : Entity<TId>(id), IHasDomainEvents
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
