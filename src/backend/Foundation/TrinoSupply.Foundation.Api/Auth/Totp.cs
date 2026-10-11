using System.Security.Cryptography;
using System.Text;

namespace TrinoSupply.Foundation.Api.Auth;

/// <summary>
/// Senha de uso único por tempo (TOTP, RFC 6238), a mesma que o Google Authenticator, o
/// Microsoft Authenticator e o Authy calculam: HMAC-SHA1 do contador de 30 segundos, seis
/// dígitos. Sem biblioteca de propósito — são trinta linhas, e o vetor de teste da RFC prova
/// que estão certas.
/// </summary>
public static class Totp
{
    public const int Digitos = 6;
    public const int PassoSegundos = 30;

    /// <summary>Segredo novo: 160 bits, em Base32, que é o que o aplicativo lê do QR e do texto.</summary>
    public static string NovoSegredo() => Base32.Codificar(RandomNumberGenerator.GetBytes(20));

    public static string Codigo(string segredoBase32, DateTimeOffset instante) =>
        Codigo(Base32.Decodificar(segredoBase32), instante.ToUnixTimeSeconds() / PassoSegundos);

    private static string Codigo(byte[] chave, long contador)
    {
        var mensagem = new byte[8];
        for (var i = 7; i >= 0; i--) { mensagem[i] = (byte)(contador & 0xff); contador >>= 8; }
        var hash = HMACSHA1.HashData(chave, mensagem);
        var offset = hash[^1] & 0x0f;
        var binario = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binario % 1_000_000).ToString("D6");
    }

    /// <summary>
    /// Confere o código aceitando o passo anterior e o seguinte: o relógio do telefone e o do
    /// servidor não batem ao segundo, e o código digitado no fim do passo chega no começo do
    /// próximo. A comparação é em tempo constante.
    /// </summary>
    public static bool Confere(string segredoBase32, string? codigo, DateTimeOffset agora, int janela = 1)
    {
        var digitado = (codigo ?? "").Replace(" ", "").Trim();
        if (digitado.Length != Digitos || !digitado.All(char.IsAsciiDigit)) return false;
        var chave = Base32.Decodificar(segredoBase32);
        var contador = agora.ToUnixTimeSeconds() / PassoSegundos;
        var esperado = Encoding.ASCII.GetBytes(digitado);
        var bate = false;
        for (var d = -janela; d <= janela; d++)
            bate |= CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Codigo(chave, contador + d)), esperado);
        return bate;
    }

    /// <summary>A URL que o QR carrega e que todo aplicativo autenticador entende.</summary>
    public static string UrlOtpauth(string emissor, string conta, string segredoBase32) =>
        $"otpauth://totp/{Uri.EscapeDataString(emissor)}:{Uri.EscapeDataString(conta)}" +
        $"?secret={segredoBase32}&issuer={Uri.EscapeDataString(emissor)}&algorithm=SHA1&digits={Digitos}&period={PassoSegundos}";
}

/// <summary>Base32 (RFC 4648), o alfabeto dos segredos TOTP. Sem preenchimento, como os aplicativos esperam.</summary>
public static class Base32
{
    private const string Alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Codificar(byte[] dados)
    {
        var sb = new StringBuilder((dados.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in dados)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(Alfabeto[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Alfabeto[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] Decodificar(string texto)
    {
        var limpo = texto.Trim().TrimEnd('=').ToUpperInvariant().Replace(" ", "");
        var saida = new List<byte>(limpo.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in limpo)
        {
            var valor = Alfabeto.IndexOf(c);
            if (valor < 0) throw new FormatException("Segredo fora do alfabeto Base32.");
            buffer = (buffer << 5) | valor; bits += 5;
            if (bits >= 8) { saida.Add((byte)((buffer >> (bits - 8)) & 0xff)); bits -= 8; }
        }
        return saida.ToArray();
    }
}
