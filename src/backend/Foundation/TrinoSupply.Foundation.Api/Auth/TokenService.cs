using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TrinoSupply.Foundation.Api.Domain;

namespace TrinoSupply.Foundation.Api.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "trino-supply";
    public string Audience { get; set; } = "trino-supply";
    public int AccessTokenMinutes { get; set; } = 15;   // FD-001-01 / MMS-004-09 §12.4
    public int RefreshTokenDays { get; set; } = 7;
}

public class TokenService(JwtOptions options)
{
    public JwtOptions Options => options;

    public string CreateAccessToken(User user, DateTimeOffset now)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("name", user.Name),
            new(ClaimTypes.Role, user.Role),
            new("modules", string.Join(',', AppModules.EffectiveFor(user))),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.UtcDateTime.AddMinutes(options.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Token do Portal do Fornecedor: papel "Supplier" + supplierId; nenhum acesso interno.</summary>
    public string CreateSupplierToken(Guid supplierId, string supplierName, DateTimeOffset now)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, supplierId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("name", supplierName),
            new("supplierId", supplierId.ToString()),
            new(ClaimTypes.Role, "Supplier"),
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret));
        var token = new JwtSecurityToken(
            issuer: options.Issuer, audience: options.Audience, claims: claims,
            notBefore: now.UtcDateTime, expires: now.UtcDateTime.AddMinutes(60),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string GenerateRefreshTokenValue()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    /// <summary>Hash SHA-256 (hex) — o valor em claro nunca é persistido.</summary>
    public static string HashRefreshToken(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static TokenValidationParameters BuildValidationParameters(JwtOptions options) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
    };
}
