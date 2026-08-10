using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace TrinoSupply.Api.Procurement;

/// <summary>Dados de uma parte (comprador/fornecedor) no cabeçalho da OC.</summary>
public sealed record OcParty(
    string Code, string Name, string TaxId, string StateRegistration, string Address, string District,
    string City, string State, string ZipCode, string Phone, string Email);

/// <summary>Linha da OC já com os valores calculados.</summary>
public sealed record OcLine(
    decimal Quantity, string Unit, string ItemCode, string Description, DateTimeOffset? DeliveryDate,
    decimal UnitPrice, decimal ServiceValue, decimal IrrfPercent, decimal IssPercent, decimal IrrfValue, decimal IssValue);

/// <summary>Documento completo da Ordem de Compra para renderização em PDF (modelo OC 664 do grupo Trino).</summary>
public sealed record OcData(
    long Number, DateTimeOffset IssuedAt, string IssuedBy, OcParty Buyer, OcParty Supplier,
    string PaymentTerms, string PaymentMethod, string FreightTerms, IReadOnlyList<OcLine> Lines,
    decimal ProductsValue, decimal IpiValue, decimal IcmsValue, decimal DiscountValue, decimal OtherExpenses,
    decimal NetValue);

/// <summary>Gera o PDF da Ordem de Compra no formato do modelo do grupo Trino (OC 664).</summary>
public static class OcPdf
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    private static string Money(decimal v) => v.ToString("N2", Br);
    private static string Pct(decimal v) => v.ToString("0.##", Br);
    private static string Qty(decimal v) => v.ToString("0.######", Br);
    private static string Date(DateTimeOffset? d) => d?.ToString("dd/MM/yyyy", Br) ?? "";

    public static byte[] Build(OcData oc)
    {
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(t => t.FontSize(8).FontColor(Colors.Black));

                page.Header().Element(h => Header(h, oc));
                page.Content().Element(c => Content(c, oc));
                page.Footer().Element(f => Footer(f, oc));
            });
        }).GeneratePdf();
    }

    private static void Header(IContainer container, OcData oc)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                // Bloco do comprador (empresa pagadora selecionada na emissão).
                row.RelativeItem(2).Column(c =>
                {
                    c.Item().Text(oc.Buyer.Name).Bold().FontSize(11);
                    c.Item().Text($"CNPJ: {oc.Buyer.TaxId}   Inscr.Est.: {Dash(oc.Buyer.StateRegistration)}");
                    c.Item().Text(oc.Buyer.Address);
                    c.Item().Text($"{JoinNonEmpty(oc.Buyer.District, $"{oc.Buyer.City}-{oc.Buyer.State}")}   CEP: {oc.Buyer.ZipCode}");
                    c.Item().Text($"Fone: {Dash(oc.Buyer.Phone)}   {Dash(oc.Buyer.Email)}");
                });

                // Bloco do documento.
                row.RelativeItem(1).AlignRight().Column(c =>
                {
                    c.Item().Text($"ORDEM DE COMPRA").Bold().FontSize(12);
                    c.Item().Text($"N.º {oc.Number}").Bold().FontSize(14);
                    c.Item().Text($"Emissão: {Date(oc.IssuedAt)}");
                });
            });

            col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Darken1);
        });
    }

    private static void Content(IContainer container, OcData oc)
    {
        container.PaddingVertical(6).Column(col =>
        {
            col.Spacing(6);

            // Fornecedor (vencedor da concorrência/BID).
            col.Item().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(6).Column(c =>
            {
                c.Item().Text("FORNECEDOR").Bold();
                c.Item().Text($"Código: {oc.Supplier.Code}   {oc.Supplier.Name}").Bold();
                c.Item().Text($"CNPJ: {Dash(oc.Supplier.TaxId)}   Inscr.Est.: {Dash(oc.Supplier.StateRegistration)}");
                c.Item().Text($"{Dash(oc.Supplier.Address)}   {JoinNonEmpty(oc.Supplier.District, $"{oc.Supplier.City}-{oc.Supplier.State}")}   CEP: {Dash(oc.Supplier.ZipCode)}");
                c.Item().Text($"Fone: {Dash(oc.Supplier.Phone)}   Cond.Pgto: {Dash(oc.PaymentTerms)}   Forma: {Dash(oc.PaymentMethod)}");
            });

            // Tabela de itens.
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(1.1f);  // Qtd
                    cols.RelativeColumn(0.8f);  // U.M.
                    cols.RelativeColumn(1.3f);  // Código
                    cols.RelativeColumn(3.2f);  // Descrição
                    cols.RelativeColumn(1.2f);  // Data Entr.
                    cols.RelativeColumn(1.3f);  // Vlr.Unit
                    cols.RelativeColumn(1.4f);  // Vlr.Serviço
                    cols.RelativeColumn(0.9f);  // %IRRF
                    cols.RelativeColumn(0.9f);  // %ISS
                    cols.RelativeColumn(1.1f);  // Vlr.IRRF
                    cols.RelativeColumn(1.1f);  // Vlr.ISS
                });

                table.Header(header =>
                {
                    HeaderCell(header, "Qtd.");
                    HeaderCell(header, "U.M.");
                    HeaderCell(header, "Código");
                    HeaderCell(header, "Descrição");
                    HeaderCell(header, "Data Entr.");
                    HeaderCell(header, "Vlr.Unit.");
                    HeaderCell(header, "Vlr.Serviço");
                    HeaderCell(header, "%IRRF");
                    HeaderCell(header, "%ISS");
                    HeaderCell(header, "Vlr.IRRF");
                    HeaderCell(header, "Vlr.ISS");
                });

                foreach (var l in oc.Lines)
                {
                    BodyCell(table, Qty(l.Quantity), true);
                    BodyCell(table, l.Unit);
                    BodyCell(table, l.ItemCode);
                    BodyCell(table, l.Description, align: false);
                    BodyCell(table, Date(l.DeliveryDate), true);
                    BodyCell(table, Money(l.UnitPrice), true);
                    BodyCell(table, Money(l.ServiceValue), true);
                    BodyCell(table, Pct(l.IrrfPercent), true);
                    BodyCell(table, Pct(l.IssPercent), true);
                    BodyCell(table, Money(l.IrrfValue), true);
                    BodyCell(table, Money(l.IssValue), true);
                }
            });

            // Totais + frete + valor por extenso.
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem(1.4f).Column(c =>
                {
                    c.Item().Text($"Frete: {Dash(oc.FreightTerms)}");
                    c.Item().PaddingTop(6).Text("Valor por extenso:").Bold();
                    c.Item().Text(ValorPorExtenso.Converter(oc.NetValue));
                });

                row.RelativeItem(1).Column(c =>
                {
                    TotalLine(c, "Valor Produtos", oc.ProductsValue);
                    TotalLine(c, "IPI", oc.IpiValue);
                    TotalLine(c, "ICMS", oc.IcmsValue);
                    TotalLine(c, "Descontos", oc.DiscountValue);
                    TotalLine(c, "Outras Despesas", oc.OtherExpenses);
                    c.Item().PaddingTop(2).BorderTop(1).BorderColor(Colors.Grey.Darken1);
                    TotalLine(c, "Valor Líquido", oc.NetValue, bold: true);
                });
            });
        });
    }

    private static void Footer(IContainer container, OcData oc)
    {
        container.Column(col =>
        {
            col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            col.Item().Row(row =>
            {
                row.RelativeItem().Text($"Comprador: {Dash(oc.IssuedBy)}");
                row.RelativeItem().AlignRight().Text($"Data Emissão: {Date(oc.IssuedAt)}");
            });
        });
    }

    private static void HeaderCell(TableCellDescriptor header, string text) =>
        header.Cell().Background(Colors.Grey.Lighten2).Border(0.5f).BorderColor(Colors.Grey.Medium)
            .Padding(3).Text(text).Bold().FontSize(7);

    private static void BodyCell(TableDescriptor table, string text, bool right = false, bool align = true)
    {
        var cell = table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3);
        if (right && align) cell = cell.AlignRight();
        cell.Text(text).FontSize(7);
    }

    private static void TotalLine(ColumnDescriptor col, string label, decimal value, bool bold = false)
    {
        col.Item().Row(row =>
        {
            var l = row.RelativeItem().Text(label);
            var v = row.RelativeItem().AlignRight().Text(Money(value));
            if (bold) { l.Bold(); v.Bold(); }
        });
    }

    private static string Dash(string? s) => string.IsNullOrWhiteSpace(s) ? "-" : s;

    private static string JoinNonEmpty(params string[] parts) =>
        string.Join("  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
