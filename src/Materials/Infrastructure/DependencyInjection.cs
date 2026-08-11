using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TrinoSupply.Foundation.Infrastructure.Persistence;
using TrinoSupply.Materials.Application;
using TrinoSupply.Materials.Infrastructure.Persistence;

namespace TrinoSupply.Materials.Infrastructure;

/// <summary>Registro da infraestrutura de Materiais (ARC-004 §2). Requer AddFoundationInfrastructure antes.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddMaterialsInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");

        // Reusa o TenantConnectionInterceptor (RLS fail-closed) já registrado pelo Foundation.
        services.AddDbContext<MaterialsDbContext>((sp, options) =>
            options
                .UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__ef_migrations", "materials"))
                .AddInterceptors(sp.GetRequiredService<TenantConnectionInterceptor>()));

        services.AddScoped<IMaterialsService, MaterialsService>();
        services.AddScoped<IStockService, StockService>();
        services.AddScoped<IReplenishmentService, ReplenishmentService>();
        services.AddScoped<ICollaboratorService, CollaboratorService>();
        services.AddScoped<IConsumptionService, ConsumptionService>();
        services.AddScoped<IStockRequestService, StockRequestService>();
        return services;
    }
}
