using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Users;

public record UserError(string Code, string Message);

public class UserService(AppDbContext db, IPasswordHasher<User> hasher, TimeProvider clock)
{
    public static readonly string[] ValidRoles =
    [
        Roles.SystemAdministrator, Roles.Requester, Roles.Approver, Roles.PurchasingOfficer,
        Roles.WarehouseOperator, Roles.WarehouseSupervisor, Roles.SupplyManager, Roles.Auditor,
    ];

    public Task<List<User>> ListAsync(CancellationToken ct = default) =>
        db.Users.OrderBy(u => u.Name).ThenBy(u => u.Email).Take(500).ToListAsync(ct);

    /// <summary>Normaliza a lista de módulos autorizados; null = usa o padrão do papel.</summary>
    private static (string? csv, UserError? error) NormalizeModules(IReadOnlyList<string>? modules)
    {
        if (modules is null) return (null, null);
        var clean = modules.Select(m => m.Trim().ToUpperInvariant())
            .Where(m => m.Length > 0).Distinct().ToList();
        var invalid = clean.Where(m => !AppModules.All.Contains(m)).ToList();
        if (invalid.Count > 0)
            return (null, new("IAM-ERR-017", $"Módulos inválidos: {string.Join(", ", invalid)}."));
        return (clean.Count == 0 ? "" : string.Join(',', clean), null);
    }

    public async Task<(User? user, UserError? error)> CreateAsync(
        string email, string name, string role, string password,
        IReadOnlyList<string>? modules = null, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || !normalized.Contains('@'))
            return (null, new("IAM-ERR-010", "Informe um e-mail válido."));
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2)
            return (null, new("IAM-ERR-011", "Informe o nome do usuário."));
        if (!ValidRoles.Contains(role))
            return (null, new("IAM-ERR-012", "Papel inválido."));
        if (string.IsNullOrEmpty(password) || password.Length < 12)
            return (null, new("IAM-ERR-013", "A senha precisa ter no mínimo 12 caracteres."));
        if (await db.Users.AnyAsync(u => u.Email == normalized, ct))
            return (null, new("IAM-ERR-014", "Já existe um usuário com este e-mail."));
        var (modulesCsv, modulesError) = NormalizeModules(modules);
        if (modulesError is not null) return (null, modulesError);

        var user = new User
        {
            Email = normalized,
            Name = name.Trim(),
            Role = role,
            Modules = modulesCsv,
            CreatedAt = clock.GetUtcNow(),
            UpdatedAt = clock.GetUtcNow(),
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return (user, null);
    }

    public async Task<(User? user, UserError? error)> UpdateAsync(
        Guid id, Guid actorId, string? name, string? role, bool? active,
        IReadOnlyList<string>? modules = null, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return (null, new("IAM-ERR-404", "Usuário não encontrado."));
        if (role is not null && !ValidRoles.Contains(role))
            return (null, new("IAM-ERR-012", "Papel inválido."));
        var (modulesCsv, modulesError) = NormalizeModules(modules);
        if (modulesError is not null) return (null, modulesError);

        var losesAdmin = user.Role == Roles.SystemAdministrator &&
                         ((role is not null && role != Roles.SystemAdministrator) || active == false);
        if (losesAdmin && !await AnotherActiveAdminExistsAsync(user.Id, ct))
            return (null, new("IAM-ERR-015", "Este é o único administrador ativo: promova outro antes de rebaixá-lo ou inativá-lo."));
        if (user.Id == actorId && active == false)
            return (null, new("IAM-ERR-016", "Você não pode inativar o seu próprio usuário."));

        if (name is not null && name.Trim().Length >= 2) user.Name = name.Trim();
        if (role is not null) user.Role = role;
        if (modules is not null) user.Modules = modulesCsv;
        if (active is not null) user.Active = active.Value;
        user.UpdatedAt = clock.GetUtcNow();

        if (active == false) await RevokeSessionsAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);
        return (user, null);
    }

    /// <summary>Define nova senha e revoga todas as sessões do usuário (SEC-003).</summary>
    public async Task<UserError?> ResetPasswordAsync(Guid id, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 12)
            return new("IAM-ERR-013", "A senha precisa ter no mínimo 12 caracteres.");
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return new("IAM-ERR-404", "Usuário não encontrado.");

        user.PasswordHash = hasher.HashPassword(user, newPassword);
        user.UpdatedAt = clock.GetUtcNow();
        await RevokeSessionsAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);
        return null;
    }

    private async Task<bool> AnotherActiveAdminExistsAsync(Guid exceptId, CancellationToken ct) =>
        await db.Users.AnyAsync(u => u.Id != exceptId && u.Active && u.Role == Roles.SystemAdministrator, ct);

    private async Task RevokeSessionsAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var tokens = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in tokens) t.RevokedAt = now;
    }
}
