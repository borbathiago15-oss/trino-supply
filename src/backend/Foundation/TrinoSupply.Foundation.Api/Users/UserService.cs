using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Auth;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Users;

public record UserError(string Code, string Message);

public class UserService(AppDbContext db, IPasswordHasher<User> hasher, TimeProvider clock)
{
    public static readonly string[] ValidRoles =
    [
        Roles.SystemAdministrator, Roles.Requester, Roles.Approver, Roles.PurchasingOfficer,
        Roles.WarehouseOperator, Roles.WarehouseSupervisor, Roles.SupplyManager, Roles.Director, Roles.Auditor,
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

    /// <summary>Normaliza a lista de centros de custo vinculados (códigos).</summary>
    private static string? NormalizeCostCenters(IReadOnlyList<string>? costCenters) =>
        costCenters is null ? null
            : string.Join(',', costCenters.Select(c => c.Trim().ToUpperInvariant()).Where(c => c.Length > 0).Distinct());

    private async Task<UserError?> ValidateDirectorAsync(Guid? directorId, CancellationToken ct)
    {
        if (directorId is null) return null;
        var director = await db.Users.SingleOrDefaultAsync(u => u.Id == directorId && u.Active, ct);
        if (director is null || director.Role is not (Roles.Director or Roles.SystemAdministrator))
            return new("IAM-ERR-019", "Diretor responsável inválido: escolha um usuário ativo com papel Diretor.");
        return null;
    }

    /// <summary>
    /// Gestor de Suprimentos responsável pela 2ª alçada das compras deste comprador
    /// (a regra vive em <c>Procurement/AlcadaDoComprador.cs</c>). Só vale gente ativa com o papel:
    /// apontar para outro papel deixaria a segunda assinatura com quem não a tem.
    /// </summary>
    private async Task<UserError?> ValidateSupplyManagerAsync(Guid? supplyManagerId, CancellationToken ct)
    {
        if (supplyManagerId is null) return null;
        var gestor = await db.Users.SingleOrDefaultAsync(u => u.Id == supplyManagerId && u.Active, ct);
        if (gestor is null || gestor.Role != Roles.SupplyManager)
            return new("IAM-ERR-020",
                "Gestor responsável inválido: escolha um usuário ativo com papel Gestor de Suprimentos.");
        return null;
    }

    /// <summary>
    /// O setor de quem trabalha — o que faz o ciclo de melhoria de um setor aparecer para o
    /// colega do mesmo setor. Setor inativo não se vincula: inativar é a forma de tirar um
    /// setor de uso, e deixá-lo entrar por aqui desfaria a decisão do cadastro.
    /// </summary>
    private async Task<UserError?> ValidateSectorAsync(Guid? sectorId, CancellationToken ct)
    {
        if (sectorId is null) return null;
        if (!await db.Sectors.AnyAsync(s => s.Id == sectorId && s.Active, ct))
            return new("IAM-ERR-023", "Setor inválido: escolha um setor ativo do cadastro.");
        return null;
    }

    public async Task<(User? user, UserError? error)> CreateAsync(
        string email, string name, string role, string password,
        IReadOnlyList<string>? modules = null, IReadOnlyList<string>? costCenters = null,
        Guid? directorId = null, Guid? supplyManagerId = null, Guid? sectorId = null,
        CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || !normalized.Contains('@'))
            return (null, new("IAM-ERR-010", "Informe um e-mail válido."));
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2)
            return (null, new("IAM-ERR-011", "Informe o nome do usuário."));
        if (!ValidRoles.Contains(role))
            return (null, new("IAM-ERR-012", "Papel inválido."));
        if (await db.Users.AnyAsync(u => u.Email == normalized, ct))
            return (null, new("IAM-ERR-014", "Já existe um usuário com este e-mail."));
        var (modulesCsv, modulesError) = NormalizeModules(modules);
        if (modulesError is not null) return (null, modulesError);
        if (await ValidateDirectorAsync(directorId, ct) is { } directorError) return (null, directorError);
        if (await ValidateSupplyManagerAsync(supplyManagerId, ct) is { } gestorError) return (null, gestorError);
        if (await ValidateSectorAsync(sectorId, ct) is { } setorError) return (null, setorError);

        var user = new User
        {
            Email = normalized,
            Name = name.Trim(),
            Role = role,
            Modules = modulesCsv,
            CostCenters = NormalizeCostCenters(costCenters),
            DirectorId = directorId,
            SupplyManagerId = supplyManagerId,
            SectorId = sectorId,
            // quem cadastra escolhe a senha, então ela nasce provisória: o dono
            // troca no primeiro acesso e ninguém fica com senha de terceiro (SEC-004)
            MustChangePassword = true,
            CreatedAt = clock.GetUtcNow(),
            UpdatedAt = clock.GetUtcNow(),
        };
        if (PasswordPolicy.Validar(password, user) is { } senhaFraca) return (null, senhaFraca);
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return (user, null);
    }

    public async Task<(User? user, UserError? error)> UpdateAsync(
        Guid id, Guid actorId, string? name, string? role, bool? active,
        IReadOnlyList<string>? modules = null, IReadOnlyList<string>? costCenters = null,
        Guid? directorId = null, bool clearDirector = false,
        Guid? supplyManagerId = null, bool clearSupplyManager = false,
        Guid? sectorId = null, bool clearSector = false, CancellationToken ct = default)
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

        if (await ValidateDirectorAsync(directorId, ct) is { } directorError) return (null, directorError);
        if (await ValidateSupplyManagerAsync(supplyManagerId, ct) is { } gestorError) return (null, gestorError);
        if (await ValidateSectorAsync(sectorId, ct) is { } setorError) return (null, setorError);
        if (name is not null && name.Trim().Length >= 2) user.Name = name.Trim();
        if (role is not null) user.Role = role;
        if (modules is not null) user.Modules = modulesCsv;
        if (costCenters is not null) user.CostCenters = NormalizeCostCenters(costCenters);
        if (directorId is not null) user.DirectorId = directorId;
        else if (clearDirector) user.DirectorId = null;
        if (supplyManagerId is not null) user.SupplyManagerId = supplyManagerId;
        else if (clearSupplyManager) user.SupplyManagerId = null;
        if (sectorId is not null) user.SectorId = sectorId;
        else if (clearSector) user.SectorId = null;
        if (active is not null) user.Active = active.Value;
        user.UpdatedAt = clock.GetUtcNow();

        if (active == false) await RevokeSessionsAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);
        return (user, null);
    }

    /// <summary>Define nova senha e revoga todas as sessões do usuário (SEC-003).</summary>
    public async Task<UserError?> ResetPasswordAsync(Guid id, string newPassword, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return new("IAM-ERR-404", "Usuário não encontrado.");
        if (PasswordPolicy.Validar(newPassword, user) is { } senhaFraca) return senhaFraca;

        user.PasswordHash = hasher.HashPassword(user, newPassword);
        // senha definida por outra pessoa volta a ser provisória (SEC-004)
        user.MustChangePassword = true;
        user.UpdatedAt = clock.GetUtcNow();
        await RevokeSessionsAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>
    /// Troca de senha pelo próprio dono: exige a senha atual, tira a marca de
    /// provisória e derruba as outras sessões, mantendo só a de quem trocou.
    /// </summary>
    public async Task<(User? user, UserError? error)> ChangeOwnPasswordAsync(
        Guid id, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.Active, ct);
        if (user is null) return (null, new("IAM-ERR-404", "Usuário não encontrado."));
        if (string.IsNullOrEmpty(currentPassword) ||
            hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            return (null, new("IAM-ERR-020", "A senha atual não confere."));
        if (PasswordPolicy.Validar(newPassword, user, currentPassword) is { } senhaFraca) return (null, senhaFraca);

        var agora = clock.GetUtcNow();
        user.PasswordHash = hasher.HashPassword(user, newPassword);
        user.MustChangePassword = false;
        user.PasswordChangedAt = agora;
        user.UpdatedAt = agora;
        // a senha antiga pode ter circulado: nenhuma sessão aberta com ela continua
        await RevokeSessionsAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);
        return (user, null);
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
