using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.BuildingBlocks.Security;
using TrinoSupply.Foundation.Application.Iam;
using TrinoSupply.Foundation.Domain.Iam;
using TrinoSupply.Foundation.Infrastructure.Persistence;

namespace TrinoSupply.Foundation.Infrastructure.Iam;

/// <summary>
/// Autorização server-side deny-by-default (SEC-001). Resolve o usuário corrente pelo par
/// (tenant, subject) e soma as permissões de seus papéis ativos. Qualquer lacuna (sem tenant,
/// sem subject, usuário inativo/inexistente, sem papéis) ⇒ conjunto vazio ⇒ nega.
/// </summary>
public sealed class PermissionChecker(
    FoundationDbContext db, ITenantContext tenant, ICurrentUser currentUser) : IPermissionChecker
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();

    public async Task<bool> HasAsync(string permission, CancellationToken ct = default) =>
        (await GetPermissionsAsync(ct)).Contains(permission);

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(CancellationToken ct = default)
    {
        if (!tenant.HasTenant || !currentUser.IsAuthenticated)
            return Empty;

        var subject = currentUser.Subject;
        if (string.IsNullOrWhiteSpace(subject))
            return Empty;

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Subject == subject, ct);
        if (user is null || user.Status != UserStatus.Active)
            return Empty;

        var roleIds = user.RoleIds.Select(r => r.Value).ToHashSet();
        if (roleIds.Count == 0)
            return Empty;

        // Papéis do tenant corrente (RLS limita ao tenant); une as permissões dos papéis do usuário.
        var roles = await db.Roles.AsNoTracking().ToListAsync(ct);
        return roles.Where(r => roleIds.Contains(r.Id.Value))
            .SelectMany(r => r.Permissions)
            .ToHashSet();
    }
}
