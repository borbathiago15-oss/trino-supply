using Microsoft.Extensions.Logging;
using TrinoSupply.BuildingBlocks.Outbox;

namespace TrinoSupply.Foundation.Infrastructure.Outbox;

/// <summary>
/// Publisher inicial: registra o evento em log estruturado (sem broker). Suficiente para o piloto e
/// para os testes do relay. Em produção, substituir por um publisher de RabbitMQ (ARC-005) — o relay
/// não muda. Nunca lança em caminho normal → o relay marca como publicado.
/// </summary>
public sealed class LoggingEventPublisher(ILogger<LoggingEventPublisher> logger) : IEventPublisher
{
    public Task PublishAsync(OutboxMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("event published {Type} tenant={Tenant} aggregate={Aggregate}/{AggregateId} id={Id}",
            message.Type, message.CompanyId, message.AggregateType, message.AggregateId, message.Id);
        return Task.CompletedTask;
    }
}
