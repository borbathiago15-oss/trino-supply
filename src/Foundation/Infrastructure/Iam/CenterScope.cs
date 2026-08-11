using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.BuildingBlocks.Security;
using TrinoSupply.Foundation.Infrastructure.Persistence;

namespace TrinoSupply.Foundation.Infrastructure.Iam;

/// <summary>
/// Escopo por centro de custo do usuário corrente (v2). <c>null</c> = sem restrição (Master/Pleno);
/// conjunto = restrito àqueles códigos (Master Junior só vê/aprova os seus — 40+ centros no grupo).
/// Consumido pelos serviços de Pedido/Aprovação para filtrar listagens e barrar decisões fora do escopo.
/// </summary>
public interface ICenterScope
{
    Task<IReadOnlySet<string>?> GetAsync(CancellationToken ct = default);
}

public sealed class DbCenterScope(FoundationDbContext db, ITenantContext tenant, ICurrentUser currentUser) : ICenterScope
{
    public async Task<IReadOnlySet<string>?> GetAsync(CancellationToken ct = default)
    {
        if (!tenant.HasTenant || string.IsNullOrWhiteSpace(currentUser.Subject))
            return null; // endpoints já são protegidos por permissão; sem usuário não há escopo a aplicar

        var subject = currentUser.Subject;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Subject == subject, ct);
        if (user is null || user.CostCenterCodes.Count == 0) return null;
        return user.CostCenterCodes.ToHashSet(StringComparer.Ordinal);
    }
}
