using System.Security.Cryptography;
using System.Text;

namespace TrinoSupply.Foundation.Api.Auth;

/// <summary>
/// Cifra o segredo TOTP antes de gravá-lo. O segredo é o que gera o código: no banco em
/// claro, uma cópia da tabela de usuários bastaria para passar pela segunda etapa de todo
/// mundo. AES-GCM com chave derivada do <c>JWT_SECRET</c> — a mesma raiz que já é o segredo
/// do ambiente, sem uma variável a mais para esquecer no deploy. Trocar o JWT_SECRET invalida
/// os segredos gravados (como invalida as sessões): quem tiver MFA precisa cadastrar de novo.
/// </summary>
public class CofreDeSegredos(JwtOptions jwt)
{
    private readonly byte[] chave = SHA256.HashData(Encoding.UTF8.GetBytes("totp:" + jwt.Secret));

    public string Cifrar(string texto)
    {
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var claro = Encoding.UTF8.GetBytes(texto);
        var cifrado = new byte[claro.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using var aes = new AesGcm(chave, tag.Length);
        aes.Encrypt(nonce, claro, cifrado, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. cifrado]);
    }

    public string Decifrar(string envelope)
    {
        var bytes = Convert.FromBase64String(envelope);
        var n = AesGcm.NonceByteSizes.MaxSize; var t = AesGcm.TagByteSizes.MaxSize;
        var nonce = bytes[..n]; var tag = bytes[n..(n + t)]; var cifrado = bytes[(n + t)..];
        var claro = new byte[cifrado.Length];
        using var aes = new AesGcm(chave, t);
        aes.Decrypt(nonce, cifrado, tag, claro);
        return Encoding.UTF8.GetString(claro);
    }
}
