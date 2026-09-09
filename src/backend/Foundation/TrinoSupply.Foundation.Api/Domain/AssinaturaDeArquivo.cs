namespace TrinoSupply.Foundation.Api.Domain;

/// <summary>
/// O que o arquivo <b>é</b>, e não o que ele diz ser.
///
/// <para>
/// O <c>Content-Type</c> de um upload é escrito pelo cliente: quem envia decide o que
/// declarar. A lista de tipos aceitos, sozinha, só barra quem é honesto — um executável
/// ou uma página HTML passam declarando <c>application/pdf</c>. Aqui a decisão é pelos
/// primeiros bytes do conteúdo, que quem envia não escolhe sem trocar o arquivo.
/// </para>
///
/// <para>
/// Texto (CSV) não tem assinatura, então a regra se inverte: em vez de exigir um começo
/// conhecido, recusa-se um começo <em>perigoso</em> — qualquer binário reconhecido e
/// qualquer coisa que abra como marcação (<c>&lt;</c>), que é como HTML, SVG e XML
/// começam. É o caso em que a ausência de assinatura é a assinatura.
/// </para>
/// </summary>
public static class AssinaturaDeArquivo
{
    /// <summary>Bytes suficientes para reconhecer qualquer formato desta tabela.</summary>
    public const int BytesNecessarios = 12;

    private static readonly byte[] Pdf = "%PDF"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    // XLSX e DOCX são pacotes ZIP; os três começos cobrem arquivo normal, vazio e dividido
    private static readonly byte[][] Zip =
    [
        [0x50, 0x4B, 0x03, 0x04], [0x50, 0x4B, 0x05, 0x06], [0x50, 0x4B, 0x07, 0x08],
    ];
    // .xls antigo: documento composto OLE2
    private static readonly byte[] Ole2 = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] Riff = "RIFF"u8.ToArray();      // WEBP = RIFF....WEBP
    private static readonly byte[] Webp = "WEBP"u8.ToArray();

    private static bool Comeca(ReadOnlySpan<byte> conteudo, ReadOnlySpan<byte> marca) =>
        conteudo.Length >= marca.Length && conteudo[..marca.Length].SequenceEqual(marca);

    private static bool EhZip(ReadOnlySpan<byte> c)
    {
        foreach (var marca in Zip) if (Comeca(c, marca)) return true;
        return false;
    }

    private static bool EhWebp(ReadOnlySpan<byte> c) =>
        Comeca(c, Riff) && c.Length >= 12 && c[8..12].SequenceEqual(Webp);

    /// <summary>Algum formato binário conhecido — usado para recusar binário disfarçado de CSV.</summary>
    private static bool EhBinarioConhecido(ReadOnlySpan<byte> c) =>
        Comeca(c, Pdf) || Comeca(c, Png) || Comeca(c, Jpeg) || EhZip(c) || Comeca(c, Ole2) || EhWebp(c);

    /// <summary>
    /// O conteúdo corresponde ao tipo declarado? Tipo fora da tabela é recusado — a lista
    /// de aceitos é a de quem sabemos reconhecer, e não se aceita o que não se sabe conferir.
    /// </summary>
    public static bool Confere(string? contentType, ReadOnlySpan<byte> conteudo)
    {
        if (conteudo.IsEmpty) return false;
        return (contentType ?? "").Trim().ToLowerInvariant() switch
        {
            "application/pdf" => Comeca(conteudo, Pdf),
            "image/png" => Comeca(conteudo, Png),
            "image/jpeg" or "image/jpg" => Comeca(conteudo, Jpeg),
            "image/webp" => EhWebp(conteudo),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => EhZip(conteudo),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => EhZip(conteudo),
            // .xls aceita os dois: o OLE2 clássico e o .xlsx renomeado, que é o engano comum
            "application/vnd.ms-excel" => Comeca(conteudo, Ole2) || EhZip(conteudo),
            // CSV é texto: não tem começo obrigatório, tem começo proibido
            "text/csv" or "application/csv" => !EhBinarioConhecido(conteudo) && conteudo[0] != (byte)'<',
            _ => false,
        };
    }
}
