using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TrinoSupply.Materials.Infrastructure.Persistence;

/// <summary>Factory de design-time para o <c>dotnet ef</c> (migrations de Materiais). Ver Foundation.</summary>
public sealed class MaterialsDbContextFactory : IDesignTimeDbContextFactory<MaterialsDbContext>
{
    public MaterialsDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                   ?? "Host=localhost;Port=5432;Database=trino;Username=trino;Password=trino";

        var options = new DbContextOptionsBuilder<MaterialsDbContext>()
            .UseNpgsql(conn, npg => npg.MigrationsHistoryTable("__ef_migrations", "materials"))
            .Options;

        return new MaterialsDbContext(options);
    }
}
