using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace TrinoSupply.Foundation.Api.Infrastructure;

/// <summary>
/// Leitor mínimo de planilha para importação de cadastros: .xlsx (ZIP + XML do OpenXML,
/// primeira aba) e .csv/.txt (vírgula ou ponto e vírgula). Sem dependência externa —
/// lê só o necessário: a matriz de células como texto.
/// </summary>
public static class SpreadsheetReader
{
    private const int MaxRows = 20000;
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static List<string[]> Read(Stream stream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".csv" or ".txt" ? ReadCsv(stream) : ReadXlsx(stream);
    }

    // ---- CSV -----------------------------------------------------------------
    private static List<string[]> ReadCsv(Stream stream)
    {
        var rows = new List<string[]>();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (reader.ReadLine() is { } line && rows.Count < MaxRows)
        {
            if (line.Length == 0) continue;
            var sep = line.Count(c => c == ';') >= line.Count(c => c == ',') ? ';' : ',';
            rows.Add(SplitCsvLine(line, sep));
        }
        return rows;
    }

    private static string[] SplitCsvLine(string line, char sep)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == sep && !quoted) { cells.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(sb.ToString().Trim());
        return [.. cells];
    }

    // ---- XLSX ----------------------------------------------------------------
    private static List<string[]> ReadXlsx(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var shared = ReadSharedStrings(zip);
        var sheet = zip.GetEntry("xl/worksheets/sheet1.xml")
                    ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet"))
                    ?? throw new InvalidDataException("Planilha sem aba legível.");

        using var sheetStream = sheet.Open();
        var doc = XDocument.Load(sheetStream);
        var rows = new List<string[]>();
        foreach (var row in doc.Descendants(Main + "row").Take(MaxRows))
        {
            var cells = new SortedDictionary<int, string>();
            foreach (var c in row.Elements(Main + "c"))
            {
                var reference = (string?)c.Attribute("r") ?? "";
                var column = ColumnIndex(reference);
                var type = (string?)c.Attribute("t");
                string value;
                if (type == "inlineStr")
                    value = c.Element(Main + "is")?.Descendants(Main + "t").Aggregate("", (a, t) => a + t.Value) ?? "";
                else
                {
                    var raw = c.Element(Main + "v")?.Value ?? "";
                    value = type == "s" && int.TryParse(raw, out var idx) && idx < shared.Count ? shared[idx] : raw;
                }
                cells[column] = value.Trim();
            }
            if (cells.Count == 0) { rows.Add([]); continue; }
            var width = cells.Keys.Max() + 1;
            var line = new string[width];
            for (var i = 0; i < width; i++) line[i] = cells.TryGetValue(i, out var v) ? v : "";
            rows.Add(line);
        }
        return rows;
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        using var s = entry.Open();
        var doc = XDocument.Load(s);
        return doc.Descendants(Main + "si")
            .Select(si => si.Descendants(Main + "t").Aggregate("", (a, t) => a + t.Value))
            .ToList();
    }

    /// <summary>"B7" → 1 (índice da coluna, base zero).</summary>
    private static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var c in reference)
        {
            if (!char.IsLetter(c)) break;
            index = index * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }
        return Math.Max(0, index - 1);
    }
}
