using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.RabbitMq;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.Foundation.Infrastructure.Multitenancy;
using TrinoSupply.Foundation.Infrastructure.Outbox;
using TrinoSupply.Foundation.Infrastructure.Persistence;
using TrinoSupply.Procurement.Infrastructure.Projections;
using Xunit;
using static TrinoSupply.Integration.Tests.PilotFixture;

namespace TrinoSupply.Integration.Tests;

/// <summary>
/// Valida a Fase 4 (eventos + projeção): emitir uma OC gera <c>OrderIssued</c> no Outbox → o relay
/// publica no RabbitMQ → o <see cref="SupplierStatsConsumer"/> atualiza a projeção
/// <c>procurement.supplier_stats</c> (assíncrono, idempotente, cancelamento reverte).
/// </summary>
[Collection("pilot")]
public sealed class SupplierStatsProjectionTests(PilotFixture fixture) : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13").WithUsername("trino").WithPassword("trino").Build();

    public Task InitializeAsync() => _rabbit.StartAsync();
    public Task DisposeAsync() => _rabbit.DisposeAsync().AsTask();

    private record CompanyResp(Guid CompanyId);
    private record LoginResp(string AccessToken);
    private record UserResp(Guid UserId);
    private record RoleResp(Guid RoleId);
    private record ReqResp(Guid RequisitionId);
    private record OrderResp(Guid OrderId);
    private record SupplierRow(Guid Id, string Code);

    private const string Password = "senha12345";

    private FoundationDbContext NewFoundationContext() =>
        new(new DbContextOptionsBuilder<FoundationDbContext>().UseNpgsql(fixture.AdminConnectionString).Options,
            new NullTenantContext());

    private async Task<(int Count, decimal Total)> ReadStatsAsync(Guid company, Guid supplier)
    {
        await using var conn = new NpgsqlConnection(fixture.AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT orders_count, total_value FROM procurement.supplier_stats WHERE company_id=@c AND supplier_id=@s", conn);
        cmd.Parameters.AddWithValue("c", company);
        cmd.Parameters.AddWithValue("s", supplier);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? (r.GetInt32(0), r.GetDecimal(1)) : (0, 0m);
    }

    private async Task<(int Count, decimal Total)> PollStatsAsync(Guid company, Guid supplier, int expected)
    {
        for (var i = 0; i < 50; i++)
        {
            var s = await ReadStatsAsync(company, supplier);
            if (s.Count == expected) return s;
            await Task.Delay(100);
        }
        return await ReadStatsAsync(company, supplier);
    }

    [Fact]
    public async Task Projector_e_idempotente()
    {
        var projector = new SupplierStatsProjector(fixture.AdminConnectionString, NullLogger<SupplierStatsProjector>.Instance);
        var company = Guid.NewGuid();
        var supplier = Guid.NewGuid();
        var ev = Guid.NewGuid();

        await projector.ApplyOrderIssuedAsync(ev, company, supplier, 100m, DateTimeOffset.UtcNow);
        await projector.ApplyOrderIssuedAsync(ev, company, supplier, 100m, DateTimeOffset.UtcNow); // MESMO evento

        var s1 = await ReadStatsAsync(company, supplier);
        Assert.Equal(1, s1.Count);
        Assert.Equal(100m, s1.Total);

        await projector.ApplyOrderIssuedAsync(Guid.NewGuid(), company, supplier, 50m, DateTimeOffset.UtcNow);
        var s2 = await ReadStatsAsync(company, supplier);
        Assert.Equal(2, s2.Count);
        Assert.Equal(150m, s2.Total);
    }

    [Fact]
    public async Task Fluxo_async_emite_projeta_e_cancela_reverte()
    {
        var options = new RabbitMqOptions
        {
            Host = _rabbit.Hostname, Port = _rabbit.GetMappedPublicPort(5672),
            User = "trino", Password = "trino", Exchange = "trino.events",
        };

        // Consumidor ligado ANTES de publicar (declara/binda a fila). Usa a role trino_app (RLS efetivo).
        var projector = new SupplierStatsProjector(fixture.AppConnectionString, NullLogger<SupplierStatsProjector>.Instance);
        await using var consumer = new SupplierStatsConsumer(options, projector, NullLogger<SupplierStatsConsumer>.Instance);
        await consumer.StartAsync();

        var c = fixture.Client();

        // Provisiona empresa + admin; cria aprovador distinto (SoD).
        var (_, company) = await PostAsync<CompanyResp>(c, "/api/v1/companies", new
        {
            legalName = "Proj Co", taxId = $"7{Guid.NewGuid():N}"[..14], adminSubject = $"adm-{Guid.NewGuid():N}",
            adminEmail = $"{Guid.NewGuid():N}@proj.com", adminName = "Adm", adminPassword = Password,
        });
        var companyId = company!.CompanyId;
        var (_, login) = await PostAsync<LoginResp>(c, "/api/v1/auth/login",
            new { companyId, email = await AdminEmail(companyId), password = Password });
        var admin = login!.AccessToken;

        var aprov1 = await ApproverAsync(c, admin, companyId, "aprovProj1", "aprov1@proj.com");
        var aprov2 = await ApproverAsync(c, admin, companyId, "aprovProj2", "aprov2@proj.com");

        // Cadastros + solicitação (2 níveis) + aprovação + emissão.
        await PostStatusAsync(c, "/api/v1/purchases/paying-companies",
            new { code = "EP1", legalName = "Pagadora", taxId = "05.345.258/0004-96" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/cost-centers", new { code = "CC1", name = "Centro" }, admin);
        await PostStatusAsync(c, "/api/v1/purchases/suppliers",
            new { code = "FORN1", name = "Fornecedor Um", taxId = "17.381.510/0001-59" }, admin);

        var (_, req) = await PostAsync<ReqResp>(c, "/api/v1/purchases/requisitions", new
        {
            payingCompanyCode = "EP1", costCenterCode = "CC1", priority = "Normal", justification = "compra",
            approverLevel1Subject = "aprovProj1", approverLevel2Subject = "aprovProj2",
            lines = new[] { new { itemCode = "ITEM-1", quantity = 4, unit = "un" } },
        }, admin);
        await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{req!.RequisitionId}/submit", null, admin);
        await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{req.RequisitionId}/approve", null, aprov1);
        await PostStatusAsync(c, $"/api/v1/purchases/requisitions/{req.RequisitionId}/approve", null, aprov2);

        var (issueStatus, order) = await PostAsync<OrderResp>(c, $"/api/v1/purchases/requisitions/{req.RequisitionId}/order", new
        {
            payingCompanyCode = "EP1", supplierCode = "FORN1",
            lines = new[] { new { itemCode = "ITEM-1", unitPrice = 25.00m } }, // 4 × 25 = 100
        }, admin);
        Assert.Equal(201, issueStatus);

        // Descobre o supplierId (chave da projeção).
        var (_, suppliers) = await GetAsync<SupplierRow[]>(c, "/api/v1/purchases/suppliers", admin);
        var supplierId = suppliers!.Single(s => s.Code == "FORN1").Id;

        // Relay publica os pendentes do Outbox (inclui OrderIssued) → consumidor projeta.
        var publisher = new RabbitMqEventPublisher(options, NullLogger<RabbitMqEventPublisher>.Instance);
        await using (var db = NewFoundationContext())
        {
            var relay = new OutboxRelay(db, publisher, new SystemClock(), NullLogger<OutboxRelay>.Instance);
            await relay.DrainOnceAsync();
        }

        var afterIssue = await PollStatsAsync(companyId, supplierId, expected: 1);
        Assert.Equal(1, afterIssue.Count);
        Assert.Equal(100m, afterIssue.Total);

        // Cancela a OC → OrderCancelled → publica → projeção reverte para 0.
        Assert.Equal(204, await PostStatusAsync(c, $"/api/v1/purchases/orders/{order!.OrderId}/cancel",
            new { reason = "teste de reversão" }, admin));
        await using (var db2 = NewFoundationContext())
        {
            var relay2 = new OutboxRelay(db2, publisher, new SystemClock(), NullLogger<OutboxRelay>.Instance);
            await relay2.DrainOnceAsync();
        }

        var afterCancel = await PollStatsAsync(companyId, supplierId, expected: 0);
        Assert.Equal(0, afterCancel.Count);

        await consumer.StopAsync();
    }

    private async Task<string> ApproverAsync(HttpClient c, string admin, Guid company, string subject, string email)
    {
        var (_, user) = await PostAsync<UserResp>(c, "/api/v1/users", new { subject, email, displayName = subject, password = Password }, admin);
        var (_, role) = await PostAsync<RoleResp>(c, "/api/v1/roles", new { name = $"Ap-{subject}" }, admin);
        await PostStatusAsync(c, $"/api/v1/roles/{role!.RoleId}/permissions", new { permission = "purchases.read" }, admin);
        await PostStatusAsync(c, $"/api/v1/roles/{role.RoleId}/permissions", new { permission = "purchases.approve" }, admin);
        await PostStatusAsync(c, $"/api/v1/users/{user!.UserId}/roles", new { roleId = role.RoleId }, admin);
        var (_, l) = await PostAsync<LoginResp>(c, "/api/v1/auth/login", new { companyId = company, email, password = Password });
        return l!.AccessToken;
    }

    // O e-mail do admin foi gerado aleatoriamente no provisionamento; recupera-o do banco (auditoria/infra).
    private async Task<string> AdminEmail(Guid companyId)
    {
        await using var conn = new NpgsqlConnection(fixture.AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT email FROM foundation.app_user WHERE company_id=@c ORDER BY email LIMIT 1", conn);
        cmd.Parameters.AddWithValue("c", companyId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }
}
