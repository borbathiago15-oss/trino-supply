namespace TrinoSupply.Foundation.Application.Iam;

/// <summary>
/// Verificação de autorização server-side (SEC-001, deny-by-default). Resolve o usuário corrente
/// (tenant + subject do JWT), soma as permissões de seus papéis e responde se possui a permissão.
/// Ausência de tenant, de usuário ou da permissão ⇒ <c>false</c> (nega).
/// </summary>
public interface IPermissionChecker
{
    Task<bool> HasAsync(string permission, CancellationToken ct = default);

    /// <summary>Permissões efetivas do usuário corrente (união dos papéis). Usada pela UI (endpoint /me).</summary>
    Task<IReadOnlySet<string>> GetPermissionsAsync(CancellationToken ct = default);
}
