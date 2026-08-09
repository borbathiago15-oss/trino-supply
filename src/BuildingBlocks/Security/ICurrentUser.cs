namespace TrinoSupply.BuildingBlocks.Security;

/// <summary>
/// Identidade autenticada da requisição, derivada do JWT (claim <c>sub</c>) — SEC-001/SEC-004.
/// Distinta do tenant (ITenantContext): aqui é QUEM; lá é de QUAL empresa. Ambos vêm do token.
/// </summary>
public interface ICurrentUser
{
    /// <summary>Identificador do sujeito no provedor de identidade (claim <c>sub</c>).</summary>
    string? Subject { get; }
    bool IsAuthenticated { get; }
}
