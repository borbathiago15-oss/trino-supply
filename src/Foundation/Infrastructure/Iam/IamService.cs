using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Multitenancy;
using TrinoSupply.BuildingBlocks.Security;
using TrinoSupply.Foundation.Application.Audit;
using TrinoSupply.Foundation.Application.Iam;
using TrinoSupply.Foundation.Domain.Audit;
using TrinoSupply.Foundation.Domain.Iam;
using TrinoSupply.Foundation.Domain.Organization;
using TrinoSupply.Foundation.Infrastructure.Persistence;

namespace TrinoSupply.Foundation.Infrastructure.Iam;

/// <summary>Implementação dos casos de uso de IAM (FD-001-01) sobre o <see cref="FoundationDbContext"/>.</summary>
public sealed class IamService(
    FoundationDbContext db, ITenantContext tenant, IUsageMetrics metrics, IAuditLog audit, IClock clock,
    IPasswordHasher passwordHasher) : IIamService
{
    public async Task<Result<Guid>> RegisterCompanyWithAdminAsync(
        string legalName, string taxId, string adminSubject, string adminEmail, string adminName,
        string adminPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(adminPassword) || adminPassword.Length < 8)
            return Result.Failure<Guid>(new Error("iam.password.weak", "Senha do admin deve ter ao menos 8 caracteres."));

        Company company;
        try
        {
            company = Company.Register(legalName, taxId);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<Guid>(new Error("iam.company.invalid", ex.Message));
        }

        var roleResult = Role.Create(company.Id, "Administrador");
        if (roleResult.IsFailure) return Result.Failure<Guid>(roleResult.Error);
        var adminRole = roleResult.Value;
        foreach (var p in PermissionCatalog.All) adminRole.Grant(p);

        var userResult = User.Register(company.Id, adminSubject, adminEmail, adminName);
        if (userResult.IsFailure) return Result.Failure<Guid>(userResult.Error);
        var admin = userResult.Value;
        admin.AssignRole(adminRole.Id);
        admin.SetPasswordHash(passwordHasher.Hash(adminPassword));

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Bootstrap: define o tenant desta transação para que o RLS (WITH CHECK) das tabelas
        // role/app_user aceite as inserções da nova empresa. is_local=true → escopo da transação.
        await db.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_company', {0}, true)", company.Id.Value.ToString());

        db.Companies.Add(company);
        db.Roles.Add(adminRole);
        db.Users.Add(admin);

        // Auditoria do provisionamento (ator = system; não há usuário autenticado no bootstrap).
        db.AuditEntries.Add(AuditEntry.Create(company.Id, clock.UtcNow, "system",
            "company.provisioned", "Company", company.Id.Value.ToString()));
        db.AuditEntries.Add(AuditEntry.Create(company.Id, clock.UtcNow, "system",
            "user.registered", "User", admin.Id.Value.ToString()));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        metrics.Record("company.provisioned", company.Id.Value.ToString());
        metrics.Record("user.registered", company.Id.Value.ToString());
        return Result.Success(company.Id.Value);
    }

    public async Task<Result<Guid>> RegisterUserAsync(
        string subject, string email, string displayName, string? password, CancellationToken ct = default)
    {
        if (!tenant.HasTenant)
            return Result.Failure<Guid>(new Error("iam.no_tenant", "Requisição sem tenant."));

        var result = User.Register(tenant.CompanyId, subject, email, displayName);
        if (result.IsFailure) return Result.Failure<Guid>(result.Error);

        if (!string.IsNullOrEmpty(password))
        {
            if (password.Length < 8)
                return Result.Failure<Guid>(new Error("iam.password.weak", "Senha deve ter ao menos 8 caracteres."));
            result.Value.SetPasswordHash(passwordHasher.Hash(password));
        }

        db.Users.Add(result.Value);
        audit.Record("user.registered", "User", result.Value.Id.Value.ToString(), new { email });
        await db.SaveChangesAsync(ct);

        metrics.Record("user.registered", tenant.CompanyId.Value.ToString());
        return Result.Success(result.Value.Id.Value);
    }

    public async Task<IReadOnlyList<UserView>> ListUsersAsync(CancellationToken ct = default)
    {
        // Tenant-scoped por RLS + (defesa em profundidade) sem exposição cross-tenant.
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync(ct);
        return users.Select(u => new UserView(
            u.Id.Value, u.Subject, u.Email, u.DisplayName, u.Status.ToString(),
            u.RoleIds.Select(r => r.Value).ToList())).ToList();
    }

    public async Task<IReadOnlyList<UserView>> ListUsersWithPermissionAsync(string permission, CancellationToken ct = default)
    {
        // Papéis do tenant (RLS) que concedem a permissão → usuários ativos que carregam algum deles.
        var roles = await db.Roles.AsNoTracking().ToListAsync(ct);
        var roleIdsWith = roles.Where(r => r.Permissions.Contains(permission)).Select(r => r.Id.Value).ToHashSet();
        if (roleIdsWith.Count == 0) return [];

        var users = await db.Users.AsNoTracking().OrderBy(u => u.DisplayName).ToListAsync(ct);
        return users
            .Where(u => u.Status == UserStatus.Active && u.RoleIds.Any(r => roleIdsWith.Contains(r.Value)))
            .Select(u => new UserView(
                u.Id.Value, u.Subject, u.Email, u.DisplayName, u.Status.ToString(),
                u.RoleIds.Select(r => r.Value).ToList()))
            .ToList();
    }

    public async Task<Result<Guid>> CreateRoleAsync(string name, CancellationToken ct = default)
    {
        if (!tenant.HasTenant)
            return Result.Failure<Guid>(new Error("iam.no_tenant", "Requisição sem tenant."));

        var result = Role.Create(tenant.CompanyId, name);
        if (result.IsFailure) return Result.Failure<Guid>(result.Error);

        db.Roles.Add(result.Value);
        audit.Record("role.created", "Role", result.Value.Id.Value.ToString(), new { name });
        await db.SaveChangesAsync(ct);
        metrics.Record("role.created", tenant.CompanyId.Value.ToString());
        return Result.Success(result.Value.Id.Value);
    }

    public async Task<IReadOnlyList<RoleView>> ListRolesAsync(CancellationToken ct = default)
    {
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        return roles.Select(r => new RoleView(r.Id.Value, r.Name, r.Permissions.ToList())).ToList();
    }

    public async Task<Result> GrantPermissionAsync(Guid roleId, string permission, CancellationToken ct = default)
    {
        var role = await db.Roles.FindAsync([RoleId.From(roleId)], ct);
        if (role is null)
            return Result.Failure(new Error("iam.role.not_found", "Papel não encontrado."));

        var granted = role.Grant(permission);
        if (granted.IsFailure) return granted;

        audit.Record("role.permission_granted", "Role", roleId.ToString(), new { permission });
        await db.SaveChangesAsync(ct);
        metrics.Record("role.permission_granted", role.CompanyId.Value.ToString());
        return Result.Success();
    }

    public async Task<Result> RevokePermissionAsync(Guid roleId, string permission, CancellationToken ct = default)
    {
        var role = await db.Roles.FindAsync([RoleId.From(roleId)], ct);
        if (role is null)
            return Result.Failure(new Error("iam.role.not_found", "Papel não encontrado."));

        role.Revoke(permission);
        audit.Record("role.permission_revoked", "Role", roleId.ToString(), new { permission });
        await db.SaveChangesAsync(ct);
        metrics.Record("role.permission_revoked", role.CompanyId.Value.ToString());
        return Result.Success();
    }

    public async Task<Result> SetUserStatusAsync(Guid userId, bool active, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([UserId.From(userId)], ct);
        if (user is null)
            return Result.Failure(new Error("iam.user.not_found", "Usuário não encontrado."));

        if (active) user.Activate(); else user.Deactivate();

        // Bloqueio corta a sessão: revoga os refresh tokens do usuário (o access token expira em minutos).
        if (!active)
        {
            var now = clock.UtcNow;
            var tokens = await db.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
            foreach (var t in tokens) t.Revoke(now);
        }

        audit.Record(active ? "user.activated" : "user.deactivated", "User", userId.ToString());
        await db.SaveChangesAsync(ct);
        metrics.Record(active ? "user.activated" : "user.deactivated", user.CompanyId.Value.ToString());
        return Result.Success();
    }

    public async Task<Result> AssignRoleToUserAsync(Guid userId, Guid roleId, CancellationToken ct = default)
    {
        // O papel precisa existir no tenant (RLS garante o escopo); evita atribuir papel inexistente.
        var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == RoleId.From(roleId), ct);
        if (role is null)
            return Result.Failure(new Error("iam.role.not_found", "Papel não encontrado."));

        var user = await db.Users.FindAsync([UserId.From(userId)], ct);
        if (user is null)
            return Result.Failure(new Error("iam.user.not_found", "Usuário não encontrado."));

        user.AssignRole(RoleId.From(roleId));
        audit.Record("user.role_assigned", "User", userId.ToString(), new { roleId });
        await db.SaveChangesAsync(ct);
        metrics.Record("user.role_assigned", user.CompanyId.Value.ToString());
        return Result.Success();
    }

    public async Task<Result> RemoveRoleFromUserAsync(Guid userId, Guid roleId, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([UserId.From(userId)], ct);
        if (user is null)
            return Result.Failure(new Error("iam.user.not_found", "Usuário não encontrado."));

        user.RemoveRole(RoleId.From(roleId));
        audit.Record("user.role_removed", "User", userId.ToString(), new { roleId });
        await db.SaveChangesAsync(ct);
        metrics.Record("user.role_removed", user.CompanyId.Value.ToString());
        return Result.Success();
    }

    public async Task<IReadOnlyList<AuditView>> ListAuditAsync(int limit, CancellationToken ct = default)
    {
        // Tenant-scoped por RLS; mais recentes primeiro. Append-only: só leitura.
        var take = limit is <= 0 or > 500 ? 100 : limit;
        var entries = await db.AuditEntries.AsNoTracking()
            .OrderByDescending(a => a.OccurredAt)
            .Take(take)
            .ToListAsync(ct);
        return entries.Select(a => new AuditView(
            a.Id, a.OccurredAt, a.ActorSubject, a.Action, a.TargetType, a.TargetId, a.Metadata)).ToList();
    }
}
