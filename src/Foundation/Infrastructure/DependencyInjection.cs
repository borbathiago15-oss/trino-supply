using Microsoft.Extensions.DependencyInjection;

namespace TrinoSupply.Foundation.Infrastructure;

/// <summary>
/// Registro de serviços de infraestrutura do Foundation (EF Core, cache, mensageria).
/// Placeholder do esqueleto — as portas são implementadas na sequência do GO-001.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddFoundationInfrastructure(this IServiceCollection services)
    {
        // TODO(GO-001 · sprint 1): DbContext (Npgsql) + RLS, Redis, Outbox/RabbitMQ, Audit.
        return services;
    }
}
