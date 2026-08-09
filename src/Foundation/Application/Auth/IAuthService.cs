using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Foundation.Application.Auth;

/// <summary>Par de tokens emitido no login/refresh.</summary>
public sealed record AuthTokens(
    string AccessToken, DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

/// <summary>
/// Autenticação (IdP local — FD-001-01, SEC-001). Login por e-mail/senha dentro de um tenant,
/// emissão de access token curto + refresh token rotacionável. O tenant vem no payload porque o
/// login é pré-autenticação (o e-mail é único por empresa, não globalmente).
/// </summary>
public interface IAuthService
{
    Task<Result<AuthTokens>> LoginAsync(Guid companyId, string email, string password, CancellationToken ct = default);

    /// <summary>Troca um refresh token válido por um novo par (rotação: o antigo é revogado).</summary>
    Task<Result<AuthTokens>> RefreshAsync(Guid companyId, string refreshToken, CancellationToken ct = default);

    /// <summary>Revoga um refresh token (logout).</summary>
    Task<Result> LogoutAsync(Guid companyId, string refreshToken, CancellationToken ct = default);
}
