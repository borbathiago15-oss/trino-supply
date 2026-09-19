using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// As demais suítes rodam em EF InMemory, onde migration nenhuma é exercitada. Este teste sobe um
/// Postgres 16 de verdade (Testcontainers) e faz o que o <c>Program.cs</c> faz no startup:
/// <c>MigrateAsync</c> do zero. É o que descobre, antes do deploy, uma migration que não aplica
/// ou um modelo que já se afastou da última migration.
/// </summary>
public sealed class MigrationsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16").Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    [Fact]
    public async Task Todas_as_migrations_aplicam_num_Postgres_real_e_o_modelo_nao_tem_desvio()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .Options;
        await using var db = new AppDbContext(options);

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(
            db.Database.GetMigrations().Count(),
            (await db.Database.GetAppliedMigrationsAsync()).Count());

        // O mesmo que `dotnet ef migrations has-pending-model-changes`, que o CLAUDE.md pede
        // antes de todo PR que toca o modelo — aqui ninguém precisa lembrar de rodar.
        Assert.False(db.Database.HasPendingModelChanges(),
            "o modelo mudou depois da última migration: gere uma com `dotnet ef migrations add`");
    }
}
