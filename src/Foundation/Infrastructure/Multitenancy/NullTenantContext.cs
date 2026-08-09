using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Foundation.Infrastructure.Multitenancy;

/// <summary>
/// Placeholder do esqueleto: sem tenant resolvido. Na sprint 1 é substituído por um
/// contexto request-scoped que lê o <c>company_id</c> do JWT (FD-001-01) e aplica o
/// GUC <c>app.current_company</c> para a RLS (deploy/db/rls.sql).
/// </summary>
public sealed class NullTenantContext : ITenantContext
{
    public bool HasTenant => false;

    public CompanyId CompanyId =>
        throw new InvalidOperationException("Nenhum tenant no contexto (esqueleto). Configure a AuthN/JWT.");
}
