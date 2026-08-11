using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TrinoSupply.Api.Security;
using Xunit;

namespace TrinoSupply.Integration.Tests;

/// <summary>Guard SEC-004: em produção a app recusa iniciar como papel privilegiado; aceita trino_app.</summary>
[Collection("pilot")]
public sealed class DatabasePrivilegeGuardTests(PilotFixture fixture)
{
    private sealed class FakeEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static DatabasePrivilegeGuard Guard(string? cs, string env)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = cs })
            .Build();
        return new DatabasePrivilegeGuard(config, new FakeEnv(env), NullLogger<DatabasePrivilegeGuard>.Instance);
    }

    [Fact]
    public async Task Producao_aceita_papel_trino_app()
    {
        // trino_app é NOSUPERUSER NOBYPASSRLS → não lança.
        await Guard(fixture.AppConnectionString, "Production").StartAsync(default);
    }

    [Fact]
    public async Task Producao_recusa_papel_superuser()
    {
        // A connection string admin do fixture é o superuser do container → deve abortar.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Guard(fixture.AdminConnectionString, "Production").StartAsync(default));
        Assert.Contains("SEC-004", ex.Message);
    }

    [Fact]
    public async Task Em_development_nao_verifica()
    {
        // Em dev o guard nem abre conexão (connection string inválida não importa).
        await Guard("Host=inexistente;Database=x;Username=y;Password=z", "Development").StartAsync(default);
    }
}
