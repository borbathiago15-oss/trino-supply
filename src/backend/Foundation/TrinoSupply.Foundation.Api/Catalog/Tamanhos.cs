namespace TrinoSupply.Foundation.Api.Catalog;

/// <summary>
/// Tamanho de EPI e fardamento. O catálogo guarda <b>um produto por tamanho</b> — a bota 38
/// e a bota 39 têm código, preço e C.A. próprios, porque são compras diferentes —, e o que
/// os mantém juntos é o <see cref="CatalogItem.BaseCode"/>. Esta classe é a regra dessa
/// convenção: como o código e a descrição da variante se formam, e em que ordem os tamanhos
/// aparecem.
///
/// <para>
/// A ordem importa: alfabética daria "G, GG, M, P", que não é grade nenhuma. Letra vem pela
/// sequência de vestuário, número pelo valor, e o que não é nem um nem outro vai para o fim,
/// em ordem alfabética — tamanho estranho não some, só não atrapalha.
/// </para>
/// </summary>
public static class Tamanhos
{
    /// <summary>A grade de letras, do menor para o maior.</summary>
    public static readonly string[] Letras = ["PP", "P", "M", "G", "GG", "XG", "XXG"];

    /// <summary>A grade numérica usual de calçado.</summary>
    public static readonly string[] Numeros =
        ["34", "35", "36", "37", "38", "39", "40", "41", "42", "43", "44", "45", "46"];

    /// <summary>O que separa a descrição do produto do tamanho dele.</summary>
    public const string Sufixo = " — Tam. ";

    /// <summary>
    /// A lista digitada vira grade: em caixa alta, sem repetição, sem espaço sobrando e na
    /// ordem da grade — quem digita "M,P,G" quer a grade, não a ordem em que lembrou dela.
    /// Aceita vírgula, ponto e vírgula, barra e quebra de linha.
    /// </summary>
    public static List<string> Normalizar(string? lista) =>
        (lista ?? string.Empty)
            .Split([',', ';', '/', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToUpperInvariant())
            .Distinct()
            .OrderBy(Ordem).ThenBy(t => t, StringComparer.Ordinal)
            .ToList();

    /// <summary>Posição do tamanho na grade: letra pela sequência, número pelo valor, o resto no fim.</summary>
    public static int Ordem(string? tamanho)
    {
        if (string.IsNullOrWhiteSpace(tamanho)) return -1;         // produto sem tamanho vem primeiro
        var t = tamanho.Trim().ToUpperInvariant();
        var letra = Array.IndexOf(Letras, t);
        if (letra >= 0) return letra;
        if (int.TryParse(t, out var numero)) return 100 + numero;
        return 1000;
    }

    /// <summary>Código da variante: o código-base com o tamanho colado (12003 + P = 12003-P).</summary>
    public static string CodigoDaVariante(string codigoBase, string? tamanho) =>
        (tamanho is null ? codigoBase : $"{codigoBase}-{tamanho}").ToUpperInvariant();

    /// <summary>Descrição da variante: a do produto mais o tamanho, legível na lista e na O.C.</summary>
    public static string DescricaoDaVariante(string descricao, string? tamanho) =>
        tamanho is null ? descricao.Trim() : $"{descricao.Trim()}{Sufixo}{tamanho}";

    /// <summary>
    /// A descrição sem o tamanho, para o seletor mostrar "Bota de segurança" uma vez em vez
    /// de sete linhas quase iguais. Derivada, e não guardada: vale para o que já está
    /// cadastrado, sem migrar nada.
    /// </summary>
    public static string DescricaoBase(string descricao, string? tamanho)
    {
        if (string.IsNullOrWhiteSpace(tamanho)) return descricao.Trim();
        var sufixo = Sufixo + tamanho.Trim().ToUpperInvariant();
        return descricao.TrimEnd().EndsWith(sufixo, StringComparison.OrdinalIgnoreCase)
            ? descricao.TrimEnd()[..^sufixo.Length].TrimEnd()
            : descricao.Trim();
    }
}
