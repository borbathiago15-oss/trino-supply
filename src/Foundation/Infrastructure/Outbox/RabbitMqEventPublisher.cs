using System.Text;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using TrinoSupply.BuildingBlocks.Outbox;

namespace TrinoSupply.Foundation.Infrastructure.Outbox;

/// <summary>Configuração do broker (RabbitMq:*). Host vazio ⇒ usa o publisher de log (sem broker).</summary>
public sealed class RabbitMqOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "trino";
    public string Password { get; set; } = "trino";
    public string Exchange { get; set; } = "trino.events";
}

/// <summary>
/// Publisher real de eventos no RabbitMQ (ARC-005). Exchange <c>topic</c> durável; routing key = nome
/// de negócio do evento; mensagem persistente com <c>MessageId = EventId</c> (idempotência no consumidor).
/// Conexão/canal preguiçosos e reutilizados; em erro, o canal é descartado e o relay reprograma (retry).
/// </summary>
public sealed class RabbitMqEventPublisher(RabbitMqOptions options, ILogger<RabbitMqEventPublisher> logger)
    : IEventPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(OutboxMessage message, CancellationToken ct = default)
    {
        var channel = await EnsureChannelAsync(ct);
        var props = new BasicProperties
        {
            Persistent = true,
            MessageId = message.Id.ToString(),
            ContentType = "application/json",
            Type = message.Type,
        };

        try
        {
            await channel.BasicPublishAsync(
                exchange: options.Exchange,
                routingKey: message.Type,
                mandatory: false,
                basicProperties: props,
                body: Encoding.UTF8.GetBytes(message.Payload),
                cancellationToken: ct);
        }
        catch
        {
            await ResetAsync(); // força reconexão no próximo evento
            throw;             // o relay conta a falha e reprograma
        }
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;

        await _gate.WaitAsync(ct);
        try
        {
            if (_channel is { IsOpen: true }) return _channel;

            var factory = new ConnectionFactory
            {
                HostName = options.Host!,
                Port = options.Port,
                UserName = options.User,
                Password = options.Password,
            };
            _connection = await factory.CreateConnectionAsync(ct);
            _channel = await _connection.CreateChannelAsync(cancellationToken: ct);
            await _channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false,
                cancellationToken: ct);
            logger.LogInformation("RabbitMQ conectado ({Host}:{Port}), exchange '{Exchange}'.",
                options.Host, options.Port, options.Exchange);
            return _channel;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ResetAsync()
    {
        try { if (_channel is not null) await _channel.DisposeAsync(); } catch { /* ignore */ }
        try { if (_connection is not null) await _connection.DisposeAsync(); } catch { /* ignore */ }
        _channel = null;
        _connection = null;
    }

    public async ValueTask DisposeAsync() => await ResetAsync();
}
