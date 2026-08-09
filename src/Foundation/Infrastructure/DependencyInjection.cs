using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.Foundation.Application.Audit;
using TrinoSupply.Foundation.Application.Iam;
using TrinoSupply.Foundation.Infrastructure.Audit;
using TrinoSupply.Foundation.Infrastructure.Iam;
using TrinoSupply.Foundation.Infrastructure.Multitenancy;
using TrinoSupply.Foundation.Infrastructure.Observability;
using TrinoSupply.Foundation.Infrastructure.Persistence;

namespace TrinoSupply.Foundation.Infrastructure;

/// <summary>Registro de serviços de infraestrutura do Foundation (ARC-004 §2).</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddFoundationInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IClock, SystemClock>();

        // Placeholder até a AuthN/JWT resolver o tenant (FD-001-01). O host (Api) sobrepõe por
        // HttpTenantContext; se nada sobrepuser, NullTenantContext → RLS fail-closed (SEC-004).
        services.TryAddScoped<ITenantContext, NullTenantContext>();

        // Enforcement do RLS: seta app.current_company por conexão (SEC-004). Scoped porque depende
        // do ITenantContext da requisição.
        services.AddScoped<TenantConnectionInterceptor>();

        var connectionString = configuration.GetConnectionString("Postgres");

        // Overload (sp, options): permite anexar o interceptor SCOPED resolvido do escopo da requisição.
        services.AddDbContext<FoundationDbContext>((sp, options) =>
            options
                .UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__ef_migrations", "foundation"))
                .AddInterceptors(sp.GetRequiredService<TenantConnectionInterceptor>()));

        // IAM (FD-001-01) + métricas de uso (case de sucesso).
        services.AddSingleton<IUsageMetrics, LoggingUsageMetrics>();
        services.AddScoped<IAuditLog, DbAuditLog>();
        services.AddScoped<IIamService, IamService>();
        services.AddScoped<IPermissionChecker, PermissionChecker>();

        // TODO(GO-001 · sprint 1): Redis, publisher Outbox→RabbitMQ, Audit.
        return services;
    }
}
