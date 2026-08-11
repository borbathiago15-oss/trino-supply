using TrinoSupply.BuildingBlocks;
using TrinoSupply.Foundation.Application.Audit;

namespace TrinoSupply.Foundation.Application.Iam;

/// <summary>Representação de leitura de um usuário (FD-001-01).</summary>
public sealed record UserView(Guid Id, string Subject, string Email, string DisplayName, string Status, IReadOnlyList<Guid> RoleIds);

/// <summary>Representação de leitura de um papel (FD-001-01).</summary>
public sealed record RoleView(Guid Id, string Name, IReadOnlyList<string> Permissions);

/// <summary>
/// Casos de uso de IAM do Foundation (FD-001-01). Provisão de empresa (bootstrap com admin) e
/// gestão de usuários do tenant corrente. Autorização é aplicada na borda (endpoints), exceto o
/// provisionamento de empresa, que é operação de plataforma.
/// </summary>
public interface IIamService
{
    /// <summary>
    /// Provisiona uma empresa: cria a Company, um papel "Administrador" com todas as permissões e
    /// vincula o primeiro usuário admin ao <paramref name="adminSubject"/>. Operação de plataforma.
    /// </summary>
    Task<Result<Guid>> RegisterCompanyWithAdminAsync(
        string legalName, string taxId, string adminSubject, string adminEmail, string adminName,
        string adminPassword, CancellationToken ct = default);

    /// <summary>Cria um usuário (sem papéis) no tenant corrente. Senha opcional (IdP local).</summary>
    Task<Result<Guid>> RegisterUserAsync(
        string subject, string email, string displayName, string? password, CancellationToken ct = default);

    /// <summary>Lista usuários do tenant corrente.</summary>
    Task<IReadOnlyList<UserView>> ListUsersAsync(CancellationToken ct = default);

    /// <summary>
    /// Lista os usuários ativos do tenant que possuem a <paramref name="permission"/> informada
    /// (via seus papéis). Usado para oferecer os candidatos a aprovador na criação da solicitação
    /// sem expor o diretório completo de usuários.
    /// </summary>
    Task<IReadOnlyList<UserView>> ListUsersWithPermissionAsync(string permission, CancellationToken ct = default);

    // ---- Gestão de papéis (tenant corrente) ----

    Task<Result<Guid>> CreateRoleAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<RoleView>> ListRolesAsync(CancellationToken ct = default);
    Task<Result> GrantPermissionAsync(Guid roleId, string permission, CancellationToken ct = default);
    Task<Result> RevokePermissionAsync(Guid roleId, string permission, CancellationToken ct = default);

    /// <summary>Atribui um papel do tenant a um usuário do tenant.</summary>
    Task<Result> AssignRoleToUserAsync(Guid userId, Guid roleId, CancellationToken ct = default);
    Task<Result> RemoveRoleFromUserAsync(Guid userId, Guid roleId, CancellationToken ct = default);

    /// <summary>
    /// Bloqueia (<paramref name="active"/>=false) ou reativa um usuário. Usuário bloqueado não loga,
    /// não renova sessão e perde todas as permissões (deny-by-default) — spec "Bloqueado: SIM".
    /// </summary>
    Task<Result> SetUserStatusAsync(Guid userId, bool active, CancellationToken ct = default);

    /// <summary>Lê a trilha de auditoria do tenant corrente (mais recentes primeiro).</summary>
    Task<IReadOnlyList<AuditView>> ListAuditAsync(int limit, CancellationToken ct = default);
}
