using TrinoSupply.Foundation.Infrastructure;
using TrinoSupply.Foundation.Infrastructure.Outbox;
using TrinoSupply.Procurement.Infrastructure.Projections;

var builder = Host.CreateApplicationBuilder(args);

// Infra do Foundation (DbContext + relay do Outbox + publisher). ARC-005 §3.
builder.Services.AddFoundationInfrastructure(builder.Configuration);
builder.Services.AddHostedService<OutboxRelayWorker>();

// Consumidor de eventos → projeção de histórico por fornecedor (ARC-005). Só com broker configurado;
// o RabbitMqOptions é registrado pelo AddFoundationInfrastructure quando RabbitMq:Host está definido.
if (!string.IsNullOrWhiteSpace(builder.Configuration["RabbitMq:Host"]))
{
    var pg = builder.Configuration.GetConnectionString("Postgres")!;
    builder.Services.AddSingleton(sp => new SupplierStatsProjector(
        pg, sp.GetRequiredService<ILogger<SupplierStatsProjector>>()));
    builder.Services.AddSingleton<SupplierStatsConsumer>();
    builder.Services.AddHostedService<SupplierStatsConsumerWorker>();
}

var host = builder.Build();
host.Run();

/// <summary>Hospeda o <see cref="SupplierStatsConsumer"/>: assina o broker no start, encerra no stop.</summary>
internal sealed class SupplierStatsConsumerWorker(SupplierStatsConsumer consumer) : IHostedService
{
    public Task StartAsync(CancellationToken ct) => consumer.StartAsync(ct);
    public Task StopAsync(CancellationToken ct) => consumer.StopAsync();
}

/// <summary>
/// Publisher do Transactional Outbox (ARC-005 §3): a cada ciclo, drena as mensagens pendentes e as
/// entrega ao IEventPublisher. Resolve o <see cref="OutboxRelay"/> (scoped) num escopo por ciclo.
/// </summary>
internal sealed class OutboxRelayWorker(IServiceScopeFactory scopes, ILogger<OutboxRelayWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxRelayWorker iniciado (intervalo {Interval}s).", Interval.TotalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var relay = scope.ServiceProvider.GetRequiredService<OutboxRelay>();
                var published = await relay.DrainOnceAsync(stoppingToken);
                if (published > 0)
                    logger.LogInformation("Outbox: {Count} evento(s) publicado(s).", published);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Ciclo do relay do Outbox falhou; novo ciclo em {Interval}s.", Interval.TotalSeconds);
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
