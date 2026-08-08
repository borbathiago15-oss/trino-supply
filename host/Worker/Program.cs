var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<OutboxRelayWorker>();
// TODO(GO-001 · sprint 1): consumir Outbox → publicar no RabbitMQ (at-least-once, DLQ, idempotência — ARC-005).

var host = builder.Build();
host.Run();

/// <summary>
/// Publisher do Transactional Outbox (ARC-005 §3). Placeholder do esqueleto:
/// na sprint 1 relê a tabela `outbox` e publica no RabbitMQ.
/// </summary>
internal sealed class OutboxRelayWorker(ILogger<OutboxRelayWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // TODO: SELECT eventos não publicados → PUBLISH → marcar publicado (ARC-005 §3).
            logger.LogDebug("OutboxRelayWorker heartbeat (skeleton).");
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
