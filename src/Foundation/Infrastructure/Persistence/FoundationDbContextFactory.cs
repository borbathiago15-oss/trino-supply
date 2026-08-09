using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TrinoSupply.Foundation.Infrastructure.Multitenancy;

namespace TrinoSupply.Foundation.Infrastructure.Persistence;

/// <summary>
/// Factory de design-time para o <c>dotnet ef</c> (migrations). Constrói o contexto sem o host/DI.
/// Não precisa de tenant real (migrations são DDL); usa <see cref="NullTenantContext"/> e NÃO anexa
/// o TenantConnectionInterceptor (SEC-004) — migrations rodam com role privilegiada, não como app.
/// A connection string vem de <c>ConnectionStrings__Postgres</c> (ou um default local).
/// </summary>
public sealed class FoundationDbContextFactory : IDesignTimeDbContextFactory<FoundationDbContext>
{
    public FoundationDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                   ?? "Host=localhost;Port=5432;Database=trino;Username=trino;Password=trino";

        var options = new DbContextOptionsBuilder<FoundationDbContext>()
            .UseNpgsql(conn, npg => npg.MigrationsHistoryTable("__ef_migrations", "foundation"))
            .Options;

        return new FoundationDbContext(options, new NullTenantContext());
    }
}
