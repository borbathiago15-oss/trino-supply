using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TrinoSupply.Foundation.Infrastructure.Outbox;

namespace TrinoSupply.Procurement.Infrastructure.Projections;

/// <summary>
/// Consumidor de eventos (ARC-005): assina o exchange <c>trino.events</c> e mantém a projeção de
/// histórico por fornecedor atualizada a partir de <c>OrderIssued</c>/<c>OrderCancelled</c>. Fila
/// durável dedicada; ack manual após projetar (at-least-once + idempotência no projetor). É um objeto
/// autônomo (Start/Stop) para ser hospedado no Worker e exercitado em testes de integração.
/// </summary>
public sealed class SupplierStatsConsumer(
    RabbitMqOptions options, SupplierStatsProjector projector, ILogger<SupplierStatsConsumer> logger)
    : IAsyncDisposable
{
    public const string QueueName = "trino.procurement.supplier-stats";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private IConnection? _connection;
    private IChannel? _channel;

    private sealed record IssuedDto(Guid EventId, DateTimeOffset OccurredAt, Guid CompanyId, Guid SupplierId, decimal NetValue);
    private sealed record CancelledDto(Guid EventId, Guid CompanyId, Guid SupplierId, decimal NetValue);

    public async Task StartAsync(CancellationToken ct = default)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.Host!,
            Port = options.Port,
            UserName = options.User,
            Password = options.Password,
        };
        _connection = await factory.CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

        await _channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await _channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await _channel.QueueBindAsync(QueueName, options.Exchange, nameof(Domain.OrderIssued), cancellationToken: ct);
        await _channel.QueueBindAsync(QueueName, options.Exchange, nameof(Domain.OrderCancelled), cancellationToken: ct);
        await _channel.BasicQosAsync(0, prefetchCount: 20, global: false, cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnReceivedAsync;
        await _channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, ct);

        logger.LogInformation("SupplierStatsConsumer assinando '{Exchange}' na fila '{Queue}'.", options.Exchange, QueueName);
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var channel = _channel!;
        var type = ea.BasicProperties.Type ?? ea.RoutingKey;
        var body = Encoding.UTF8.GetString(ea.Body.ToArray());

        try
        {
            switch (type)
            {
                case nameof(Domain.OrderIssued):
                {
                    var e = JsonSerializer.Deserialize<IssuedDto>(body, Json)!;
                    await projector.ApplyOrderIssuedAsync(e.EventId, e.CompanyId, e.SupplierId, e.NetValue, e.OccurredAt);
                    break;
                }
                case nameof(Domain.OrderCancelled):
                {
                    var e = JsonSerializer.Deserialize<CancelledDto>(body, Json)!;
                    await projector.ApplyOrderCancelledAsync(e.EventId, e.CompanyId, e.SupplierId, e.NetValue);
                    break;
                }
                default:
                    logger.LogDebug("Evento '{Type}' ignorado pelo consumidor de estatísticas.", type);
                    break;
            }

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (JsonException ex)
        {
            // Mensagem malformada (poison): descarta para não entrar em loop. Idempotência cobre reprocesso.
            logger.LogError(ex, "Evento '{Type}' malformado; descartado.", type);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
        }
        catch (Exception ex)
        {
            // Falha transitória (ex.: banco): reenfileira para nova tentativa.
            logger.LogError(ex, "Falha ao projetar evento '{Type}'; reenfileirado.", type);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
        }
    }

    public async Task StopAsync()
    {
        try { if (_channel is not null) await _channel.CloseAsync(); } catch { /* ignore */ }
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try { if (_channel is not null) await _channel.DisposeAsync(); } catch { /* ignore */ }
        try { if (_connection is not null) await _connection.DisposeAsync(); } catch { /* ignore */ }
        _channel = null;
        _connection = null;
    }
}
