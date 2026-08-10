using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.Foundation.Infrastructure.Multitenancy;
using TrinoSupply.Foundation.Infrastructure.Outbox;
using TrinoSupply.Foundation.Infrastructure.Persistence;
using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Valida o publisher REAL de RabbitMQ + o relay do Outbox: provisionar uma empresa gera eventos no
/// Outbox → o relay publica no broker → uma fila ligada ao exchange recebe as mensagens (ARC-005).
/// </summary>
[Collection("pilot")]
public sealed class RabbitMqPublisherTests(PilotFixture fixture) : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13")
        .WithUsername("trino")
        .WithPassword("trino")
        .Build();

    public Task InitializeAsync() => _rabbit.StartAsync();
    public Task DisposeAsync() => _rabbit.DisposeAsync().AsTask();

    private record CompanyResp(Guid CompanyId);

    private FoundationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<FoundationDbContext>()
            .UseNpgsql(fixture.AdminConnectionString).Options;
        return new FoundationDbContext(options, new NullTenantContext());
    }

    [Fact]
    public async Task Relay_publica_eventos_no_RabbitMQ()
    {
        var host = _rabbit.Hostname;
        var port = _rabbit.GetMappedPublicPort(5672);
        const string exchange = "trino.events";

        // Consumidor: declara a fila ligada ao exchange ANTES de publicar (topic, todas as rotas).
        var factory = new ConnectionFactory { HostName = host, Port = port, UserName = "trino", Password = "trino" };
        await using var conn = await factory.CreateConnectionAsync();
        await using var channel = await conn.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        var queue = await channel.QueueDeclareAsync("", durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(queue.QueueName, exchange, "#");

        // Provisiona uma empresa → grava CompanyRegistered/RoleCreated/UserRegistered no Outbox.
        var (status, _) = await PostAsync<CompanyResp>(fixture.Client(), "/api/v1/companies", new
        {
            legalName = "Rabbit Co", taxId = $"7{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail = $"{Guid.NewGuid():N}@rb.com", adminName = "Adm", adminPassword = "senha12345",
        });
        Assert.Equal(201, status);

        // Relay publica os pendentes no broker real.
        var publisher = new RabbitMqEventPublisher(
            new RabbitMqOptions { Host = host, Port = port, User = "trino", Password = "trino", Exchange = exchange },
            NullLogger<RabbitMqEventPublisher>.Instance);
        await using var db = NewContext();
        var relay = new OutboxRelay(db, publisher, new SystemClock(), NullLogger<OutboxRelay>.Instance);
        var published = await relay.DrainOnceAsync();
        Assert.True(published >= 1);

        // Consome a fila e confirma que os eventos chegaram (inclui CompanyRegistered).
        var types = new List<string>();
        for (var attempt = 0; attempt < 50 && types.Count < published; attempt++)
        {
            var got = await channel.BasicGetAsync(queue.QueueName, autoAck: true);
            if (got is null) { await Task.Delay(100); continue; }
            types.Add(got.BasicProperties.Type ?? Encoding.UTF8.GetString(got.Body.ToArray()));
        }

        Assert.NotEmpty(types);
        Assert.Contains("CompanyRegistered", types);
    }
}
