using ClosedXML.Excel;
using TrinoSupply.Materials.Application;
using TrinoSupply.Materials.Domain;

namespace TrinoSupply.Api.Materials;

/// <summary>
/// Modelo (template) e leitura da planilha de cadastro de itens/produtos em lote.
/// Colunas: <c>Código</c>, <c>Descrição</c>, <c>Unidade</c>, <c>Grupo</c>.
/// </summary>
public static class ItemExcel
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string Sheet = "Produtos";

    public static byte[] BuildTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(Sheet);
        string[] headers = ["Código", "Descrição", "Unidade", "Grupo", "C.A"];
        for (var i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.LightGray;
            c.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }
        ws.Cell(2, 1).Value = "DETERG-5L";
        ws.Cell(2, 2).Value = "Detergente neutro 5L";
        ws.Cell(2, 3).Value = "un";
        ws.Cell(2, 4).Value = "Limpeza";
        ws.Cell(2, 5).Value = "";
        ws.Columns().AdjustToContents();
        ws.Range(1, 1, 1, headers.Length).SetAutoFilter();

        var notes = wb.AddWorksheet("Instruções");
        notes.Cell(1, 1).Value = "Como usar";
        notes.Cell(1, 1).Style.Font.Bold = true;
        notes.Cell(3, 1).Value = "1. Uma linha por produto na aba \"Produtos\" (a partir da linha 2).";
        notes.Cell(4, 1).Value = "2. Código: código do produto (ERP). Descrição: nome do produto.";
        notes.Cell(5, 1).Value = "3. Unidade: un, cx, pc, kg… (se não existir, é criada automaticamente).";
        notes.Cell(6, 1).Value = "4. Grupo/família: " + string.Join(", ", ProductGroups.All) + " (aceita outros).";
        notes.Cell(7, 1).Value = "5. C.A: número do Certificado de Aprovação (apenas EPI; deixe vazio p/ fardamento).";
        notes.Cell(8, 1).Value = "6. Remova a linha de exemplo antes de importar.";
        notes.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static (IReadOnlyList<ItemImportRow> Rows, IReadOnlyList<string> Errors) Parse(Stream file)
    {
        var rows = new List<ItemImportRow>();
        var errors = new List<string>();

        XLWorkbook wb;
        try { wb = new XLWorkbook(file); }
        catch { return (rows, ["Arquivo inválido: envie um .xlsx gerado a partir do modelo."]); }

        using (wb)
        {
            var ws = wb.Worksheets.FirstOrDefault(w => string.Equals(w.Name, Sheet, StringComparison.OrdinalIgnoreCase))
                     ?? wb.Worksheets.FirstOrDefault();
            if (ws is null) return (rows, ["Planilha sem abas."]);

            var last = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (var row = 2; row <= last; row++)
            {
                var code = ws.Cell(row, 1).GetString().Trim();
                var name = ws.Cell(row, 2).GetString().Trim();
                var unit = ws.Cell(row, 3).GetString().Trim();
                var group = ws.Cell(row, 4).GetString().Trim();
                var ca = ws.Cell(row, 5).GetString().Trim();

                if (code.Length == 0 && name.Length == 0 && unit.Length == 0 && group.Length == 0) continue;
                if (code.Length == 0) { errors.Add($"Linha {row}: código vazio."); continue; }
                if (name.Length == 0) { errors.Add($"Linha {row}: descrição vazia."); continue; }

                rows.Add(new ItemImportRow(code, name, unit.Length == 0 ? "un" : unit,
                    group.Length == 0 ? null : group, ca.Length == 0 ? null : ca));
            }
        }
        return (rows, errors);
    }
}
