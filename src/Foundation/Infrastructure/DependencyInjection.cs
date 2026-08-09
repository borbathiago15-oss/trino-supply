using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.Foundation.Infrastructure.Multitenancy;
using TrinoSupply.Foundation.Infrastructure.Persistence;

namespace TrinoSupply.Foundation.Infrastructure;

/// <summary>Registro de serviços de infraestrutura do Foundation (ARC-004 §2).</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddFoundationInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IClock, SystemClock>();

        // Placeholder até a AuthN/JWT resolver o tenant (FD-001-01) — sprint 1.
        services.AddScoped<ITenantContext, NullTenantContext>();

        var connectionString = configuration.GetConnectionString("Postgres");
        services.AddDbContext<FoundationDbContext>(options =>
            options.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__ef_migrations", "foundation")));

        // TODO(GO-001 · sprint 1): ITenantContext (do JWT), Redis, publisher Outbox→RabbitMQ, Audit.
        return services;
    }
}
