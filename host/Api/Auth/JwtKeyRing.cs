using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace TrinoSupply.Api.Auth;

/// <summary>Uma chave de assinatura identificada por <c>kid</c>.</summary>
public sealed record SigningKey(string Kid, SymmetricSecurityKey Key);

/// <summary>
/// Conjunto de chaves de assinatura (SEC-001 §rotação de chaves). A primeira é a ATIVA (assina os
/// tokens novos); TODAS validam — permitindo rotacionar sem invalidar tokens ainda vivos. Config:
/// <c>Jwt:Keys:[i]:{Kid,Secret}</c> (ordenada, [0] ativa); fallback para <c>Jwt:DevSigningKey</c>.
/// </summary>
public sealed class JwtKeyRing
{
    private JwtKeyRing(string issuer, string audience, IReadOnlyList<SigningKey> keys)
    {
        Issuer = issuer;
        Audience = audience;
        Keys = keys;
    }

    public string Issuer { get; }
    public string Audience { get; }
    public IReadOnlyList<SigningKey> Keys { get; }
    public bool HasKeys => Keys.Count > 0;
    public SigningKey Active => Keys[0];
    public IReadOnlyList<SecurityKey> ValidationKeys => Keys.Select(k => (SecurityKey)k.Key).ToList();

    public static JwtKeyRing FromConfig(IConfiguration cfg)
    {
        var issuer = cfg["Jwt:Issuer"] ?? "trino-supply";
        var audience = cfg["Jwt:Audience"] ?? "trino-supply";

        var keys = new List<SigningKey>();
        foreach (var section in cfg.GetSection("Jwt:Keys").GetChildren())
        {
            var kid = section["Kid"];
            var secret = section["Secret"];
            if (!string.IsNullOrWhiteSpace(kid) && !string.IsNullOrWhiteSpace(secret))
                keys.Add(new SigningKey(kid, MakeKey(kid, secret)));
        }

        // Fallback dev: chave única com kid "dev".
        var devKey = cfg["Jwt:DevSigningKey"];
        if (keys.Count == 0 && !string.IsNullOrWhiteSpace(devKey))
            keys.Add(new SigningKey("dev", MakeKey("dev", devKey)));

        return new JwtKeyRing(issuer, audience, keys);
    }

    private static SymmetricSecurityKey MakeKey(string kid, string secret) =>
        new(Encoding.UTF8.GetBytes(secret)) { KeyId = kid };
}
