namespace TrinoSupply.BuildingBlocks.Outbox;

/// <summary>
/// Porta de publicação de eventos (ARC-005). O relay do Outbox entrega cada mensagem a esta porta;
/// a implementação real publica no broker (RabbitMQ). Deve lançar em falha (o relay conta a falha
/// e reprograma). At-least-once: o consumidor deduplica por <c>EventId</c> (idempotência).
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken ct = default);
}
