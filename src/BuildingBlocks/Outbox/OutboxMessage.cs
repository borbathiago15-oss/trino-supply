namespace TrinoSupply.BuildingBlocks.Outbox;

/// <summary>
/// Mensagem do Transactional Outbox (ARC-005 §3): gravada na MESMA transação da mudança de estado,
/// relida e publicada no RabbitMQ pelo Worker (at-least-once, DLQ, idempotência por EventId).
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Empresa (tenant) de origem — isolamento e ordenação por agregado/chave.</summary>
    public Guid CompanyId { get; init; }

    /// <summary>Nome de negócio do evento (ADR-010), ex.: "ItemActivated".</summary>
    public required string Type { get; init; }

    /// <summary>Envelope + payload serializado (JSON) conforme ARC-005 §8.</summary>
    public required string Payload { get; init; }

    public string? AggregateType { get; init; }
    public Guid? AggregateId { get; init; }
    public Guid? CorrelationId { get; init; }

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Publicação (null enquanto pendente). Publisher marca ao confirmar no broker.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    public int RetryCount { get; set; }
    public string? LastError { get; set; }

    /// <summary>Marca como publicado (idempotente): o publisher confirmou a entrega no broker.</summary>
    public void MarkPublished(DateTimeOffset now)
    {
        PublishedAt = now;
        LastError = null;
    }

    /// <summary>Registra uma falha de publicação para retry/backoff (ARC-005 §3).</summary>
    public void RecordFailure(string error)
    {
        RetryCount++;
        LastError = error.Length > 1000 ? error[..1000] : error;
    }
}

