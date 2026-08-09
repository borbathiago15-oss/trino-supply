using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Foundation.Application.Iam;

/// <summary>Representação de leitura de um usuário (FD-001-01).</summary>
public sealed record UserView(Guid Id, string Email, string DisplayName, string Status, IReadOnlyList<Guid> RoleIds);

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
        CancellationToken ct = default);

    /// <summary>Cria um usuário (sem papéis) no tenant corrente.</summary>
    Task<Result<Guid>> RegisterUserAsync(string subject, string email, string displayName, CancellationToken ct = default);

    /// <summary>Lista usuários do tenant corrente.</summary>
    Task<IReadOnlyList<UserView>> ListUsersAsync(CancellationToken ct = default);
}
