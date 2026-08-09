using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.BuildingBlocks.Security;
using TrinoSupply.Foundation.Application.Iam;
using TrinoSupply.Foundation.Domain.Iam;
using TrinoSupply.Foundation.Infrastructure.Persistence;

namespace TrinoSupply.Foundation.Infrastructure.Iam;

/// <summary>
/// Autorização server-side deny-by-default (SEC-001). Resolve o usuário corrente pelo par
/// (tenant, subject), soma as permissões de seus papéis ativos e verifica a permissão pedida.
/// Qualquer lacuna (sem tenant, sem subject, usuário inativo/inexistente, permissão ausente) ⇒ nega.
/// </summary>
public sealed class PermissionChecker(
    FoundationDbContext db, ITenantContext tenant, ICurrentUser currentUser) : IPermissionChecker
{
    public async Task<bool> HasAsync(string permission, CancellationToken ct = default)
    {
        if (!tenant.HasTenant || !currentUser.IsAuthenticated)
            return false;

        var subject = currentUser.Subject;
        if (string.IsNullOrWhiteSpace(subject))
            return false;

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Subject == subject, ct);
        if (user is null || user.Status != UserStatus.Active)
            return false;

        var roleIds = user.RoleIds.Select(r => r.Value).ToHashSet();
        if (roleIds.Count == 0)
            return false;

        // Papéis do tenant corrente (RLS já limita ao tenant); filtra pelos papéis do usuário.
        var roles = await db.Roles.AsNoTracking().ToListAsync(ct);
        return roles.Any(r => roleIds.Contains(r.Id.Value) && r.Has(permission));
    }
}
