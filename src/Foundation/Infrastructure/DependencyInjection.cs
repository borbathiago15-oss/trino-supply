using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.BuildingBlocks.Outbox;
using TrinoSupply.BuildingBlocks.Security;
using TrinoSupply.Foundation.Infrastructure.Outbox;
using TrinoSupply.Foundation.Application.Audit;
using TrinoSupply.Foundation.Application.Auth;
using TrinoSupply.Foundation.Application.Iam;
using TrinoSupply.Foundation.Infrastructure.Audit;
using TrinoSupply.Foundation.Infrastructure.Auth;
using TrinoSupply.Foundation.Infrastructure.Iam;
using TrinoSupply.Foundation.Infrastructure.Security;
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
        services.AddScoped<Audit.IBusinessAudit, Audit.BusinessAudit>();
        services.AddScoped<IIamService, IamService>();
        services.AddScoped<IPermissionChecker, PermissionChecker>();

        // AuthN (IdP local): hashing de senha + login/refresh. ITokenIssuer é provido pelo host.
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<Auth.ILoginThrottle, Auth.InMemoryLoginThrottle>();
        services.AddScoped<IAuthService, AuthService>();

        // Eventos assíncronos: relay do Outbox (ARC-005). Publisher = RabbitMQ se configurado; senão log.
        var rabbitHost = configuration["RabbitMq:Host"];
        if (!string.IsNullOrWhiteSpace(rabbitHost))
        {
            services.AddSingleton(new RabbitMqOptions
            {
                Host = rabbitHost,
                Port = int.TryParse(configuration["RabbitMq:Port"], out var p) ? p : 5672,
                User = configuration["RabbitMq:User"] ?? "trino",
                Password = configuration["RabbitMq:Password"] ?? "trino",
                Exchange = configuration["RabbitMq:Exchange"] ?? "trino.events",
            });
            services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
        }
        else
        {
            services.AddSingleton<IEventPublisher, LoggingEventPublisher>();
        }
        services.AddScoped<OutboxRelay>();

        // TODO(GO-001 · sprint 1): Redis, publisher Outbox→RabbitMQ, Audit.
        return services;
    }
}
