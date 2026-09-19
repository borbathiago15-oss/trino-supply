using System.Globalization;
using System.Xml.Linq;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.Procurement.Domain;

namespace TrinoSupply.Procurement.Infrastructure.Nfe;

/// <summary>
/// Leitura do XML da NF-e no layout da SEFAZ. Aceita tanto o documento autorizado
/// (<c>nfeProc/NFe/infNFe</c>) quanto a NF-e avulsa (<c>NFe/infNFe</c>) — na prática o comprador
/// recebe ora um, ora outro, e recusar o segundo só geraria retrabalho manual.
/// <para>
/// Lê só o que a conciliação precisa (cabeçalho, emitente, itens e total). Os tributos seguem no
/// arquivo, que é guardado inteiro: quando o fiscal precisar deles, não haverá reimportação.
/// </para>
/// </summary>
public static class NfeXmlParser
{
    private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";

    public static Result<NfeDocument> Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return Result.Failure<NfeDocument>(new Error("purchases.nfe.empty", "Arquivo XML vazio."));

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (System.Xml.XmlException ex)
        {
            return Result.Failure<NfeDocument>(new Error("purchases.nfe.malformed",
                $"XML inválido: {ex.Message}"));
        }

        var infNFe = doc.Descendants(Nfe + "infNFe").FirstOrDefault()
                     ?? doc.Descendants("infNFe").FirstOrDefault();
        if (infNFe is null)
            return Result.Failure<NfeDocument>(new Error("purchases.nfe.not_nfe",
                "O arquivo não parece ser uma NF-e (elemento infNFe não encontrado)."));

        // O Id vem como "NFe" + 44 dígitos; a chave é o que resta.
        var chave = NfeAccessKey.OnlyDigits((string?)infNFe.Attribute("Id"));
        var ide = El(infNFe, "ide");
        var emit = El(infNFe, "emit");
        if (ide is null || emit is null)
            return Result.Failure<NfeDocument>(new Error("purchases.nfe.incomplete",
                "NF-e sem bloco de identificação ou de emitente."));

        var modelo = Text(ide, "mod");
        if (string.IsNullOrWhiteSpace(modelo) && chave.Length == NfeAccessKey.Length)
            modelo = NfeAccessKey.Model(chave);

        var numero = Long(ide, "nNF");
        if (numero is null)
            return Result.Failure<NfeDocument>(new Error("purchases.nfe.incomplete", "NF-e sem número (nNF)."));

        var emissao = Date(ide, "dhEmi") ?? Date(ide, "dEmi");
        if (emissao is null)
            return Result.Failure<NfeDocument>(new Error("purchases.nfe.incomplete", "NF-e sem data de emissão."));

        var itens = new List<NfeItem>();
        foreach (var det in infNFe.Elements(Nfe + "det").Concat(infNFe.Elements("det")))
        {
            var prod = El(det, "prod");
            if (prod is null) continue;

            var numeroItem = int.TryParse((string?)det.Attribute("nItem"), out var n) ? n : itens.Count + 1;
            var quantidade = Decimal(prod, "qCom") ?? 0m;
            var unitario = Decimal(prod, "vUnCom") ?? 0m;
            itens.Add(new NfeItem(
                numeroItem,
                Text(prod, "cProd") ?? string.Empty,
                Text(prod, "xProd") ?? string.Empty,
                Text(prod, "NCM"),
                Text(prod, "CFOP"),
                Text(prod, "uCom") ?? string.Empty,
                quantidade,
                unitario,
                Decimal(prod, "vProd") ?? Math.Round(quantidade * unitario, 2, MidpointRounding.AwayFromZero)));
        }

        var total = Decimal(El(El(infNFe, "total"), "ICMSTot"), "vNF")
                    ?? itens.Sum(i => i.TotalValue);

        return Result.Success(new NfeDocument(
            chave, numero.Value, Text(ide, "serie") ?? "1", emissao.Value, modelo ?? string.Empty,
            Text(emit, "CNPJ") ?? Text(emit, "CPF") ?? string.Empty,
            Text(emit, "xNome") ?? string.Empty, total, itens));
    }

    // O XML da NF-e costuma vir com namespace, mas arquivos gerados por ferramentas internas nem
    // sempre o trazem — as duas formas são tentadas antes de desistir de um campo.
    private static XElement? El(XElement? parent, string name) =>
        parent?.Element(Nfe + name) ?? parent?.Element(name);

    private static string? Text(XElement? parent, string name)
    {
        var v = El(parent, name)?.Value?.Trim();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static long? Long(XElement? parent, string name) =>
        long.TryParse(Text(parent, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Decimais da NF-e usam ponto e até 4 casas — cultura invariante, sempre.</summary>
    private static decimal? Decimal(XElement? parent, string name) =>
        decimal.TryParse(Text(parent, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>
    /// A NF-e emite <c>dhEmi</c> no fuso local (quase sempre −03:00). Guardar em UTC é obrigatório —
    /// o Postgres só aceita offset zero em <c>timestamptz</c> — e é o que o resto do sistema faz.
    /// </summary>
    private static DateTimeOffset? Date(XElement? parent, string name)
    {
        var raw = Text(parent, name);
        if (raw is null) return null;
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            return dto.ToUniversalTime();
        // dEmi (layout 3.10) vem como data pura, sem hora nem fuso.
        return DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;
    }
}
