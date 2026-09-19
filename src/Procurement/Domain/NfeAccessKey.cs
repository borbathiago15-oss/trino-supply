using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Procurement.Domain;

/// <summary>
/// Chave de acesso da NF-e: 44 dígitos, o último deles um verificador módulo 11. Validar o DV aqui
/// custa nada e pega o erro mais comum da ingestão — chave truncada, digitada ou de outro arquivo —
/// antes de ela virar um vínculo errado entre nota e pedido.
/// <para>
/// Layout: cUF(2) AAMM(4) CNPJ(14) mod(2) série(3) nNF(9) tpEmis(1) cNF(8) cDV(1).
/// </para>
/// </summary>
public static class NfeAccessKey
{
    public const int Length = 44;

    /// <summary>Modelo 55 = NF-e (o 65 é NFC-e, consumidor final — não entra como nota de compra).</summary>
    public const string ModeloNfe = "55";

    public static Result<string> Parse(string? raw)
    {
        var chave = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
        if (chave.Length != Length)
            return Result.Failure<string>(new Error("purchases.nfe.key_length",
                $"A chave de acesso deve ter {Length} dígitos (recebidos {chave.Length})."));
        if (!IsCheckDigitValid(chave))
            return Result.Failure<string>(new Error("purchases.nfe.key_invalid",
                "Dígito verificador da chave de acesso inválido — o arquivo pode estar corrompido."));
        return Result.Success(chave);
    }

    /// <summary>Módulo 11 sobre os 43 primeiros dígitos, pesos 2..9 ciclando da direita para a esquerda.</summary>
    public static bool IsCheckDigitValid(string chave)
    {
        if (chave.Length != Length || !chave.All(char.IsDigit)) return false;

        var soma = 0;
        var peso = 2;
        for (var i = Length - 2; i >= 0; i--)
        {
            soma += (chave[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }

        var resto = soma % 11;
        var dv = resto is 0 or 1 ? 0 : 11 - resto;
        return dv == chave[Length - 1] - '0';
    }

    /// <summary>CNPJ do emitente embutido na chave (posições 6..19) — confere com o fornecedor da OC.</summary>
    public static string EmitterTaxId(string chave) =>
        chave.Length == Length ? chave.Substring(6, 14) : string.Empty;

    /// <summary>Modelo do documento (posições 20..21). Nota de compra é o 55.</summary>
    public static string Model(string chave) =>
        chave.Length == Length ? chave.Substring(20, 2) : string.Empty;

    /// <summary>Só os dígitos de um CNPJ, para comparar cadastro (formatado) com XML (sem máscara).</summary>
    public static string OnlyDigits(string? value) =>
        new((value ?? string.Empty).Where(char.IsDigit).ToArray());
}
