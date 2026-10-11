using System.Security.Cryptography;
using System.Text;
using TrinoSupply.Foundation.Api.Domain;

namespace TrinoSupply.Foundation.Api.Auth;

/// <summary>
/// A régua da verificação em duas etapas (auditoria A4). Decisão da empresa (2026-10): o
/// <b>diretor</b> e o <b>administrador</b> não entram sem a segunda etapa — são quem aprova
/// dinheiro e quem cria usuários, e senha vazada nessas contas é conta tomada; para os
/// demais papéis ela é opcional, ligada pela própria pessoa.
///
/// <para>
/// Quem tem papel que exige e ainda não cadastrou entra numa sessão presa no cadastro
/// (<see cref="ClaimCadastro"/>), do mesmo jeito que a senha provisória prende na troca: o
/// token só abre as rotas do cadastro do autenticador, e a tela leva direto a elas.
/// </para>
///
/// <para>
/// Os <b>códigos de recuperação</b> são a saída de quem perdeu o telefone: oito códigos de uso
/// único, mostrados uma vez na ativação e guardados só como hash. Sem eles, o administrador
/// redefine o autenticador na tela de Usuários — e com um administrador só, os códigos são a
/// única saída dele mesmo.
/// </para>
/// </summary>
public static class Mfa
{
    public const string ClaimCadastro = "mfa_setup";
    /// <summary>Audiência do token do meio do login: o JwtBearer da API não o aceita como sessão.</summary>
    public const string AudienciaPendente = "trino-supply:mfa";
    public const int MinutosDoTokenPendente = 5;
    public const int CodigosDeRecuperacao = 8;
    public const string Emissor = "Trino Supply";

    public static bool Exigida(string role) => role is Roles.Director or Roles.SystemAdministrator;
    public static bool Ativa(User u) => u.MfaEnabledAt is not null && !string.IsNullOrEmpty(u.TotpSecret);
    /// <summary>O papel exige e a pessoa ainda não cadastrou: a sessão fica presa no cadastro.</summary>
    public static bool PrecisaCadastrar(User u) => Exigida(u.Role) && !Ativa(u);

    /// <summary>Oito códigos novos: o que se mostra uma vez, e o que se grava (hash).</summary>
    public static (string[] EmClaro, string Hashes) NovosCodigosDeRecuperacao()
    {
        var claros = Enumerable.Range(0, CodigosDeRecuperacao).Select(_ => NovoCodigo()).ToArray();
        return (claros, string.Join(',', claros.Select(Hash)));
    }

    /// <summary>Usa um código de recuperação: vale uma vez, e some da lista.</summary>
    public static bool ConsumirCodigoDeRecuperacao(User u, string? codigo)
    {
        var normalizado = Normalizar(codigo);
        if (normalizado.Length == 0 || string.IsNullOrEmpty(u.MfaRecoveryCodes)) return false;
        var hashes = u.MfaRecoveryCodes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        var alvo = Hash(normalizado);
        var i = hashes.FindIndex(h => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(h), Encoding.ASCII.GetBytes(alvo)));
        if (i < 0) return false;
        hashes.RemoveAt(i);
        u.MfaRecoveryCodes = string.Join(',', hashes);
        return true;
    }

    public static int CodigosDeRecuperacaoRestantes(User u) =>
        string.IsNullOrEmpty(u.MfaRecoveryCodes) ? 0 : u.MfaRecoveryCodes.Split(',', StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Parece um código de recuperação (letras e dígitos, dez caracteres) e não um TOTP (seis dígitos).</summary>
    public static bool PareceCodigoDeRecuperacao(string? codigo) => Normalizar(codigo).Length == 10;

    private static string NovoCodigo()
    {
        const string alfabeto = "abcdefghjkmnpqrstuvwxyz23456789";   // sem 0/O, 1/l/i: o código é lido e digitado
        var bytes = RandomNumberGenerator.GetBytes(10);
        var sb = new StringBuilder(11);
        for (var i = 0; i < 10; i++) { if (i == 5) sb.Append('-'); sb.Append(alfabeto[bytes[i] % alfabeto.Length]); }
        return sb.ToString();
    }

    private static string Normalizar(string? codigo) =>
        new((codigo ?? "").ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

    private static string Hash(string codigo) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalizar(codigo))));
}
