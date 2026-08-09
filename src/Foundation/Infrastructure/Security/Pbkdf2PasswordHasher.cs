using System.Security.Cryptography;
using TrinoSupply.BuildingBlocks.Security;

namespace TrinoSupply.Foundation.Infrastructure.Security;

/// <summary>
/// Hash de senha PBKDF2-HMAC-SHA256 (SEC-003), sem dependência externa. Formato:
/// <c>pbkdf2$&lt;iterações&gt;$&lt;saltB64&gt;$&lt;hashB64&gt;</c> — os parâmetros ficam embutidos, o que
/// permite elevar o custo no futuro sem quebrar hashes antigos. Comparação em tempo constante.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 210_000;   // OWASP 2023 (PBKDF2-HMAC-SHA256)
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public bool Verify(string hash, string password)
    {
        var parts = hash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2") return false;
        if (!int.TryParse(parts[1], out var iterations)) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
