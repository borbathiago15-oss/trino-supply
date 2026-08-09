namespace TrinoSupply.Foundation.Application.Auth;

/// <summary>Access token emitido, com o instante de expiração.</summary>
public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Emissão de access token (JWT) — porta implementada no host de composição (que detém as chaves de
/// assinatura). O token carrega <c>sub</c> e <c>company_id</c>; a autorização é resolvida no servidor
/// pela permissão do usuário (deny-by-default), não pelo token.
/// </summary>
public interface ITokenIssuer
{
    AccessToken Issue(Guid companyId, string subject);
}
