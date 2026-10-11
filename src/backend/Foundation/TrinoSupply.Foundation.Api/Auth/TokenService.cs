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

    /// <summary>Claim que marca a sessão como presa na troca de senha obrigatória.</summary>
    public const string SenhaProvisoria = "must_change_password";

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
        // senha provisória vira claim: o filtro de rota fecha tudo menos a troca
        // de senha, sem consultar o banco a cada requisição (SEC-004)
        if (user.MustChangePassword) claims.Add(new Claim(SenhaProvisoria, "1"));
        // papel que exige a segunda etapa e ainda não a cadastrou: a sessão só abre o cadastro
        if (Mfa.PrecisaCadastrar(user)) claims.Add(new Claim(Mfa.ClaimCadastro, "1"));

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

    /// <summary>
    /// O token do meio do login: a senha conferiu e falta o código. Tem audiência própria, então
    /// o JwtBearer da API o recusa como sessão — ele só serve para <c>/auth/mfa/verify</c>, por
    /// cinco minutos, e carrega só quem é.
    /// </summary>
    public string CreateMfaToken(User user, DateTimeOffset now)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret));
        var token = new JwtSecurityToken(
            issuer: options.Issuer, audience: Mfa.AudienciaPendente,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())],
            notBefore: now.UtcDateTime, expires: now.UtcDateTime.AddMinutes(Mfa.MinutosDoTokenPendente),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Quem é o dono de um token do meio do login; nulo se expirou, foi forjado ou é um token de sessão.</summary>
    public Guid? ReadMfaToken(string token, DateTimeOffset now)
    {
        try
        {
            var parametros = BuildValidationParameters(options);
            parametros.ValidAudience = Mfa.AudienciaPendente;
            parametros.LifetimeValidator = (nbf, exp, _, _) => exp is { } fim && now.UtcDateTime < fim.AddSeconds(30) && (nbf is null || nbf.Value.AddSeconds(-30) <= now.UtcDateTime);
            var principal = new JwtSecurityTokenHandler().ValidateToken(token, parametros, out _);
            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(sub, out var id) ? id : null;
        }
        catch (Exception e) when (e is SecurityTokenException or ArgumentException)
        {
            return null;
        }
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
        // o token é assinado só com HS256: aceitar outro algoritmo não tem uso legítimo (auditoria A14)
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
    };
}
