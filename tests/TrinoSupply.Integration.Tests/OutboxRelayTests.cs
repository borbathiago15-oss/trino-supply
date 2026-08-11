using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Outbox;
using TrinoSupply.Foundation.Infrastructure.Multitenancy;
using TrinoSupply.Foundation.Infrastructure.Outbox;
using TrinoSupply.Foundation.Infrastructure.Persistence;
using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

[Collection("pilot")]
public sealed class OutboxRelayTests(PilotFixture fixture)
{
    private record CompanyResp(Guid CompanyId);

    private sealed class OkPublisher : IEventPublisher
    {
        public int Count;
        public Task PublishAsync(OutboxMessage message, CancellationToken ct = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingPublisher : IEventPublisher
    {
        public Task PublishAsync(OutboxMessage message, CancellationToken ct = default) =>
            throw new InvalidOperationException("broker indisponível");
    }

    private FoundationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<FoundationDbContext>()
            .UseNpgsql(fixture.AdminConnectionString)
            .Options;
        return new FoundationDbContext(options, new NullTenantContext());
    }

    private async Task ProvisionCompanyAsync()
    {
        // Provisionar uma empresa grava eventos de negócio no Outbox (CompanyRegistered, RoleCreated,
        // UserRegistered) na mesma transação — é a fonte de mensagens pendentes para o relay.
        var (status, _) = await PostAsync<CompanyResp>(fixture.Client(), "/api/v1/companies", new
        {
            legalName = "Outbox Co", taxId = $"9{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail = $"{Guid.NewGuid():N}@ob.com", adminName = "Adm", adminPassword = "senha12345",
        });
        Assert.Equal(201, status);
    }

    [Fact]
    public async Task Relay_publica_pendentes_e_marca_como_publicado()
    {
        await ProvisionCompanyAsync();

        await using var db = NewContext();
        var pendingBefore = await db.Outbox.CountAsync(m => m.PublishedAt == null);
        Assert.True(pendingBefore >= 1, "provisionar deve gerar mensagens no outbox");

        var publisher = new OkPublisher();
        var relay = new OutboxRelay(db, publisher, new SystemClock(), NullLogger<OutboxRelay>.Instance);

        // O relay trabalha em lotes: drena até esvaziar (como o Worker faz em ciclos).
        var published = 0;
        int batch;
        do { batch = await relay.DrainOnceAsync(); published += batch; } while (batch > 0);

        Assert.True(published >= pendingBefore, $"publicados={published} < pendentes={pendingBefore}");
        Assert.True(publisher.Count >= pendingBefore);

        await using var check = NewContext();
        Assert.Equal(0, await check.Outbox.CountAsync(m => m.PublishedAt == null && m.RetryCount < OutboxRelay.MaxRetries));
    }

    [Fact]
    public async Task Falha_do_publisher_incrementa_retry_e_nao_marca_publicado()
    {
        await ProvisionCompanyAsync();

        // Pega uma mensagem pendente específica para inspecionar depois. Tem de ser a MAIS ANTIGA:
        // o relay drena em lotes de BatchSize ordenados por occurred_at — com backlog > BatchSize
        // (a suíte inteira compartilha o banco), uma mensagem recente ficaria fora do primeiro lote.
        await using var db = NewContext();
        var target = await db.Outbox
            .Where(m => m.PublishedAt == null && m.RetryCount < OutboxRelay.MaxRetries)
            .OrderBy(m => m.OccurredAt).FirstAsync();
        var retriesBefore = target.RetryCount;

        var relay = new OutboxRelay(db, new FailingPublisher(), new SystemClock(), NullLogger<OutboxRelay>.Instance);
        var published = await relay.DrainOnceAsync();

        Assert.Equal(0, published); // nada publicado

        await using var check = NewContext();
        var after = await check.Outbox.FirstAsync(m => m.Id == target.Id);
        Assert.Null(after.PublishedAt);                 // continua pendente
        Assert.True(after.RetryCount > retriesBefore);  // falha contabilizada
        Assert.NotNull(after.LastError);
    }
}
