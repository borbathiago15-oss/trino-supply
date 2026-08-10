using ClosedXML.Excel;
using TrinoSupply.Procurement.Application;

namespace TrinoSupply.Api.Procurement;

/// <summary>
/// Modelo de planilha (template) para cadastro de itens de requisição em lote e leitura do arquivo
/// enviado pelo usuário. Colunas: <c>Código do Item</c>, <c>Quantidade</c>, <c>Unidade</c>.
/// </summary>
public static class RequisitionExcel
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string Sheet = "Itens";

    /// <summary>Gera o arquivo .xlsx de modelo (cabeçalho + linha de exemplo + instruções).</summary>
    public static byte[] BuildTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(Sheet);

        string[] headers = ["Código do Item", "Quantidade", "Unidade"];
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        // Linha de exemplo (o usuário substitui pelos itens reais).
        ws.Cell(2, 1).Value = "PARAFUSO-M6";
        ws.Cell(2, 2).Value = 100;
        ws.Cell(2, 3).Value = "un";

        ws.Column(2).Style.NumberFormat.Format = "#,##0.######";
        ws.Columns().AdjustToContents();
        ws.Range(1, 1, 1, headers.Length).SetAutoFilter();

        var notes = wb.AddWorksheet("Instruções");
        notes.Cell(1, 1).Value = "Como usar o modelo";
        notes.Cell(1, 1).Style.Font.Bold = true;
        notes.Cell(3, 1).Value = "1. Preencha uma linha por item na aba \"Itens\" (a partir da linha 2).";
        notes.Cell(4, 1).Value = "2. Código do Item: o código cadastrado no material (ex.: PARAFUSO-M6).";
        notes.Cell(5, 1).Value = "3. Quantidade: número positivo (aceita decimais).";
        notes.Cell(6, 1).Value = "4. Unidade: unidade de medida (ex.: un, kg, m).";
        notes.Cell(7, 1).Value = "5. Não altere o cabeçalho. Remova a linha de exemplo antes de importar.";
        notes.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public sealed record ParseResult(IReadOnlyList<RequisitionLineInput> Lines, IReadOnlyList<string> Errors);

    /// <summary>Lê o arquivo enviado e devolve as linhas válidas + erros por linha (não lança).</summary>
    public static ParseResult Parse(Stream file)
    {
        var lines = new List<RequisitionLineInput>();
        var errors = new List<string>();

        XLWorkbook wb;
        try
        {
            wb = new XLWorkbook(file);
        }
        catch (Exception)
        {
            return new ParseResult(lines, ["Arquivo inválido: envie um .xlsx gerado a partir do modelo."]);
        }

        using (wb)
        {
            var ws = wb.Worksheets.FirstOrDefault(w => string.Equals(w.Name, Sheet, StringComparison.OrdinalIgnoreCase))
                     ?? wb.Worksheets.FirstOrDefault();
            if (ws is null)
                return new ParseResult(lines, ["Planilha sem abas."]);

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (var row = 2; row <= lastRow; row++)
            {
                var codeCell = ws.Cell(row, 1);
                var qtyCell = ws.Cell(row, 2);
                var unitCell = ws.Cell(row, 3);

                var code = codeCell.GetString().Trim();
                var unit = unitCell.GetString().Trim();
                var qtyText = qtyCell.GetString().Trim();

                // Linha totalmente vazia → fim silencioso.
                if (code.Length == 0 && qtyText.Length == 0 && unit.Length == 0)
                    continue;

                if (code.Length == 0)
                {
                    errors.Add($"Linha {row}: código do item vazio.");
                    continue;
                }
                if (!qtyCell.TryGetValue(out decimal qty) &&
                    !decimal.TryParse(qtyText.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out qty))
                {
                    errors.Add($"Linha {row}: quantidade inválida ('{qtyText}').");
                    continue;
                }
                if (qty <= 0)
                {
                    errors.Add($"Linha {row}: quantidade deve ser positiva.");
                    continue;
                }
                if (unit.Length == 0) unit = "un";

                lines.Add(new RequisitionLineInput(code, qty, unit));
            }
        }

        return new ParseResult(lines, errors);
    }
}
