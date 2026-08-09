using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.Foundation.Application.Auth;

namespace TrinoSupply.Api.Auth;

/// <summary>
/// Emite access tokens JWT (HS256) assinados pela chave ATIVA do <see cref="JwtKeyRing"/>, com o
/// <c>kid</c> no cabeçalho para o validador escolher a chave certa (SEC-001). Curto por design.
/// </summary>
public sealed class JwtTokenIssuer(JwtKeyRing ring, IClock clock) : ITokenIssuer
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public AccessToken Issue(Guid companyId, string subject)
    {
        var now = clock.UtcNow;
        var expires = now.Add(Lifetime);
        var credentials = new SigningCredentials(ring.Active.Key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: ring.Issuer,
            audience: ring.Audience,
            claims: new[]
            {
                new Claim("sub", subject),
                new Claim("company_id", companyId.ToString())
            },
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessToken(jwt, expires);
    }
}
