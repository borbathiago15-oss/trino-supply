using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Outbox;
using TrinoSupply.Foundation.Infrastructure.Persistence;

namespace TrinoSupply.Foundation.Infrastructure.Outbox;

/// <summary>
/// Relay do Transactional Outbox (ARC-005 §3): drena mensagens pendentes e as entrega ao
/// <see cref="IEventPublisher"/>. At-least-once, ordenado por ocorrência. Falhas incrementam o
/// contador e ficam para o próximo ciclo (backoff); ao atingir <see cref="MaxRetries"/> a mensagem
/// é considerada dead-letter (parada) para inspeção — não bloqueia as demais.
/// A outbox NÃO tem RLS (infra) — o relay é um processo de sistema que lê todos os tenants (SEC-004 §6).
/// </summary>
public sealed class OutboxRelay(FoundationDbContext db, IEventPublisher publisher, IClock clock, ILogger<OutboxRelay> logger)
{
    public const int BatchSize = 100;
    public const int MaxRetries = 5;

    /// <summary>Processa um lote de pendentes. Retorna quantas foram publicadas com sucesso.</summary>
    public async Task<int> DrainOnceAsync(CancellationToken ct = default)
    {
        var pending = await db.Outbox
            .Where(m => m.PublishedAt == null && m.RetryCount < MaxRetries)
            .OrderBy(m => m.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        var published = 0;
        foreach (var message in pending)
        {
            try
            {
                await publisher.PublishAsync(message, ct);
                message.MarkPublished(clock.UtcNow);
                published++;
            }
            catch (Exception ex)
            {
                message.RecordFailure(ex.Message);
                if (message.RetryCount >= MaxRetries)
                    logger.LogError(ex, "Outbox {Id} ({Type}) em dead-letter após {Retries} tentativas.",
                        message.Id, message.Type, message.RetryCount);
                else
                    logger.LogWarning("Falha ao publicar outbox {Id} ({Type}); tentativa {Retries}.",
                        message.Id, message.Type, message.RetryCount);
            }
        }

        await db.SaveChangesAsync(ct);
        return published;
    }
}
