using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TrinoSupply.Procurement.Infrastructure.Persistence;

/// <summary>Factory de design-time para o <c>dotnet ef</c> (migrations de Compras).</summary>
public sealed class ProcurementDbContextFactory : IDesignTimeDbContextFactory<ProcurementDbContext>
{
    public ProcurementDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                   ?? "Host=localhost;Port=5432;Database=trino;Username=trino;Password=trino";

        var options = new DbContextOptionsBuilder<ProcurementDbContext>()
            .UseNpgsql(conn, npg => npg.MigrationsHistoryTable("__ef_migrations", "procurement"))
            .Options;

        return new ProcurementDbContext(options);
    }
}
