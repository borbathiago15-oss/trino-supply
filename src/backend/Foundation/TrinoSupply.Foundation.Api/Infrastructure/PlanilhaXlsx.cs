using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace TrinoSupply.Foundation.Api.Infrastructure;

/// <summary>
/// Um gerador de planilha <c>.xlsx</c> mínimo, sem biblioteca: o arquivo é um ZIP com meia dúzia
/// de XMLs, e o que o relatório precisa é uma tabela por aba — texto, número e data. Entrou aqui,
/// e não uma dependência nova, porque a planilha do relatório é o único lugar que escreve xlsx, e
/// o <see cref="SpreadsheetReader"/> da importação já lê o que este arquivo produz: há teste que
/// faz a volta inteira.
///
/// <para>
/// Texto vai <b>inline</b> (<c>t="inlineStr"</c>), sem tabela de strings compartilhadas, porque a
/// planilha é gerada uma vez e lida por uma pessoa; número vai como número, para o Excel somar;
/// data vai como texto <c>dd/MM/yyyy</c>, porque data numérica exige estilo e o leitor humano
/// não ganha nada com ela. Nulo é célula vazia.
/// </para>
/// </summary>
public static class PlanilhaXlsx
{
    /// <summary>Uma aba: o nome (até 31 caracteres, regra do Excel), o cabeçalho e as linhas.</summary>
    public sealed record Aba(string Nome, IReadOnlyList<string> Cabecalho, IEnumerable<IReadOnlyList<object?>> Linhas);

    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static byte[] Gerar(IReadOnlyList<Aba> abas)
    {
        if (abas.Count == 0) throw new ArgumentException("A planilha precisa de ao menos uma aba.", nameof(abas));
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Escrever(zip, "[Content_Types].xml", ContentTypes(abas.Count));
            Escrever(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
                + "</Relationships>");
            Escrever(zip, "xl/workbook.xml", Workbook(abas));
            Escrever(zip, "xl/_rels/workbook.xml.rels", WorkbookRels(abas.Count));
            for (var i = 0; i < abas.Count; i++)
                Escrever(zip, $"xl/worksheets/sheet{i + 1}.xml", Planilha(abas[i]));
        }
        return ms.ToArray();
    }

    private static void Escrever(ZipArchive zip, string caminho, string conteudo)
    {
        var entrada = zip.CreateEntry(caminho, CompressionLevel.Optimal);
        using var w = new StreamWriter(entrada.Open(), new UTF8Encoding(false));
        w.Write(conteudo);
    }

    private static string ContentTypes(int abas)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
            .Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">")
            .Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>")
            .Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>")
            .Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
        for (var i = 1; i <= abas; i++)
            sb.Append($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        return sb.Append("</Types>").ToString();
    }

    private static string Workbook(IReadOnlyList<Aba> abas)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
            .Append($"<workbook xmlns=\"{Main}\" xmlns:r=\"{Rel}\"><sheets>");
        for (var i = 0; i < abas.Count; i++)
            sb.Append($"<sheet name=\"{Xml(NomeDaAba(abas[i].Nome))}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        return sb.Append("</sheets></workbook>").ToString();
    }

    private static string WorkbookRels(int abas)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
            .Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (var i = 1; i <= abas; i++)
            sb.Append($"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>");
        return sb.Append("</Relationships>").ToString();
    }

    /// <summary>O Excel recusa nome de aba com mais de 31 caracteres ou com <c>[]:*?/\</c>.</summary>
    private static string NomeDaAba(string nome)
    {
        var limpo = new string(nome.Where(c => c is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\')).ToArray()).Trim();
        if (limpo.Length == 0) limpo = "Planilha";
        return limpo.Length <= 31 ? limpo : limpo[..31];
    }

    private static string Planilha(Aba aba)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
            .Append($"<worksheet xmlns=\"{Main}\">");
        // largura fixa e legível para todas as colunas: a planilha abre sem ninguém arrastar a borda
        sb.Append($"<cols><col min=\"1\" max=\"{Math.Max(1, aba.Cabecalho.Count)}\" width=\"22\" customWidth=\"1\"/></cols>");
        sb.Append("<sheetData>");
        var linha = 1;
        sb.Append(Linha(linha++, aba.Cabecalho.Select(c => (object?)c).ToList()));
        foreach (var valores in aba.Linhas) sb.Append(Linha(linha++, valores));
        return sb.Append("</sheetData></worksheet>").ToString();
    }

    private static string Linha(int numero, IReadOnlyList<object?> valores)
    {
        var sb = new StringBuilder($"<row r=\"{numero}\">");
        for (var c = 0; c < valores.Count; c++)
        {
            var referencia = $"{Coluna(c)}{numero}";
            switch (valores[c])
            {
                case null: break;
                case string texto: sb.Append(Texto(referencia, texto)); break;
                case bool b: sb.Append(Texto(referencia, b ? "Sim" : "Não")); break;
                case DateOnly d: sb.Append(Texto(referencia, d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture))); break;
                case DateTimeOffset dt: sb.Append(Texto(referencia, dt.UtcDateTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture))); break;
                case DateTime dt: sb.Append(Texto(referencia, dt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture))); break;
                case decimal n: sb.Append(Numero(referencia, n.ToString(CultureInfo.InvariantCulture))); break;
                case double n: sb.Append(Numero(referencia, n.ToString("R", CultureInfo.InvariantCulture))); break;
                case float n: sb.Append(Numero(referencia, n.ToString("R", CultureInfo.InvariantCulture))); break;
                case int n: sb.Append(Numero(referencia, n.ToString(CultureInfo.InvariantCulture))); break;
                case long n: sb.Append(Numero(referencia, n.ToString(CultureInfo.InvariantCulture))); break;
                case Guid g: sb.Append(Texto(referencia, g.ToString())); break;
                default: sb.Append(Texto(referencia, Convert.ToString(valores[c], CultureInfo.InvariantCulture) ?? "")); break;
            }
        }
        return sb.Append("</row>").ToString();
    }

    private static string Texto(string referencia, string texto) =>
        $"<c r=\"{referencia}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Xml(texto)}</t></is></c>";

    private static string Numero(string referencia, string valor) => $"<c r=\"{referencia}\"><v>{valor}</v></c>";

    /// <summary>A1, B1… Z1, AA1: a referência da célula, da coluna zero em diante.</summary>
    public static string Coluna(int indice)
    {
        var sb = new StringBuilder();
        var n = indice;
        do { sb.Insert(0, (char)('A' + n % 26)); n = n / 26 - 1; } while (n >= 0);
        return sb.ToString();
    }

    private static string Xml(string texto)
    {
        // caracteres de controle não cabem em XML 1.0: a planilha abriria com erro por causa de um \u0001
        var limpo = new string(texto.Where(c => !char.IsControl(c) || c is '\t' or '\n' or '\r').ToArray());
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true, ConformanceLevel = ConformanceLevel.Fragment }))
            w.WriteString(limpo);
        return sb.ToString();
    }
}
