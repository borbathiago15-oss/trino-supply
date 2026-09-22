using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TrinoSupply.Foundation.Api.Domain;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// PDF da Ordem de Compra no modelo oficial do Grupo Trino (RFQ-001 §6):
/// página 1 — cabeçalho da empresa, bloco do fornecedor, dados de entrega,
/// cláusulas padrão, observações, itens, totais, valor líquido + por extenso;
/// página 2 — política de pagamento a fornecedores (do cadastro Empresa).
/// </summary>
public static class PurchaseOrderPdf
{
    public static byte[] Generate(PurchaseOrder order, Supplier supplier, CompanyProfile? company, Quotation? quotation)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var c = company ?? new CompanyProfile();
        var net = order.TotalValue;

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(t => t.FontSize(8.5f));

                page.Header().Element(h => Header(h, c));

                page.Content().Column(col =>
                {
                    col.Spacing(6);

                    // título
                    col.Item().Border(1.4f).Padding(4).AlignCenter()
                        .Text($"ORDEM DE COMPRA N.º   {order.Number}").Bold().FontSize(12);

                    // bloco do fornecedor
                    col.Item().Border(0.8f).Padding(6).Column(f =>
                    {
                        f.Spacing(2);
                        f.Item().Row(r =>
                        {
                            r.RelativeItem(7).Text(t => { t.Span("Fornecedor............:  ").SemiBold(); t.Span($"{supplier.TradeName ?? supplier.LegalName}  ({supplier.LegalName})"); });
                            r.RelativeItem(4).Text(t => { t.Span("CNPJ:  ").SemiBold(); t.Span(FormatTaxId(supplier.TaxId)); });
                        });
                        f.Item().Row(r =>
                        {
                            r.RelativeItem(7).Text(t => { t.Span("E-mail..................:  ").SemiBold(); t.Span(supplier.Email ?? "—"); });
                            r.RelativeItem(4).Text(t => { t.Span("Fone....................:  ").SemiBold(); t.Span(supplier.Phone ?? "—"); });
                        });
                        f.Item().Row(r =>
                        {
                            r.RelativeItem(7).Text(t => { t.Span("Cond.Pgto...........:  ").SemiBold(); t.Span(order.PaymentTerms ?? "a combinar"); });
                            r.RelativeItem(4).Text(t => { t.Span("Prazo de entrega:  ").SemiBold(); t.Span(order.DeliveryDays is not null ? $"{order.DeliveryDays} dias" : "a combinar"); });
                        });
                    });

                    // dados de entrega
                    col.Item().Border(0.8f).Padding(6).Row(r =>
                    {
                        r.ConstantItem(90).Text("Dados de Entrega").SemiBold();
                        r.RelativeItem().Column(d =>
                        {
                            d.Item().Text(c.LegalName is { Length: > 0 } ? c.LegalName : "—").SemiBold();
                            d.Item().Text(c.DeliveryAddress ?? $"{c.Address} — {c.District} — {c.City}-{c.State} CEP: {c.Zip}");
                            d.Item().Text(t => { t.Span("CNPJ:  ").SemiBold(); t.Span(c.DeliveryTaxId ?? c.TaxId); });
                        });
                    });

                    // cláusulas padrão
                    if (!string.IsNullOrWhiteSpace(c.StandardClauses))
                        col.Item().PaddingVertical(2).Text(c.StandardClauses).FontSize(7.5f).Italic();

                    // observações
                    col.Item().Column(o =>
                    {
                        o.Item().Text("OBSERVAÇÕES").Bold();
                        o.Item().MinHeight(20).Text(order.Notes ?? " ");
                        o.Item().LineHorizontal(0.5f);
                    });

                    // referências do processo
                    col.Item().DefaultTextStyle(x => x.FontSize(8)).Text(t =>
                    {
                        t.Span("Processo:  ").SemiBold();
                        t.Span($"Solicitação {order.SourcePrNumber ?? "—"}   ·   Cotação {order.QuotationNumber ?? "—"}");
                        // compra dividida: esta O.C. atende só as famílias ganhas por este fornecedor
                        if (!string.IsNullOrWhiteSpace(order.Families)) t.Span($"   ·   Família(s): {order.Families}");
                        if (quotation?.SelectedByLabel is not null) t.Span($"   ·   Comprador: {order.IssuedByLabel}");
                    });

                    // itens
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(38);   // Qtd
                            cd.ConstantColumn(30);   // U.M.
                            cd.ConstantColumn(62);   // Produto (código)
                            cd.RelativeColumn(5);    // Descrição
                            cd.ConstantColumn(64);   // Vlr. Unitário
                            cd.ConstantColumn(66);   // Vlr. Produto
                        });
                        table.Header(h =>
                        {
                            foreach (var head in new[] { "Qtd.", "U.M.", "Produto", "Descrição do Produto", "Vlr. Unitário", "Vlr. Produto" })
                                h.Cell().BorderBottom(1).PaddingVertical(2).Text(head).SemiBold().FontSize(8);
                        });
                        foreach (var i in order.Items)
                        {
                            table.Cell().PaddingVertical(2).Text($"{i.Quantity:0.##}");
                            table.Cell().PaddingVertical(2).Text(i.UnitOfMeasure);
                            table.Cell().PaddingVertical(2).Text(i.CatalogCode ?? "—");
                            table.Cell().PaddingVertical(2).Text(i.Description);
                            table.Cell().PaddingVertical(2).AlignRight().Text($"{i.UnitPrice ?? 0:N2}");
                            table.Cell().PaddingVertical(2).AlignRight().Text($"{(i.UnitPrice ?? 0) * i.Quantity:N2}");
                        }
                    });

                    // totais
                    var products = order.Items.Sum(i => (i.UnitPrice ?? 0) * i.Quantity);
                    col.Item().BorderTop(1).PaddingTop(4).Row(r =>
                    {
                        r.RelativeItem().Column(l =>
                        {
                            l.Item().Text(t => { t.Span("Valor Produtos.........:  ").SemiBold(); t.Span($"{products:N2}"); });
                            l.Item().Text(t => { t.Span("Valor do Frete...........:  ").SemiBold(); t.Span($"{order.FreightValue ?? 0:N2}"); });
                        });
                        r.RelativeItem().Column(l =>
                        {
                            l.Item().Text(t => { t.Span("Condição de Pagamento:  ").SemiBold(); t.Span(order.PaymentTerms ?? "a combinar"); });
                            l.Item().Text(t => { t.Span("Prazo de Entrega............:  ").SemiBold(); t.Span(order.DeliveryDays is not null ? $"{order.DeliveryDays} dias" : "a combinar"); });
                        });
                    });

                    col.Item().Border(1).Padding(5).Row(r =>
                    {
                        r.RelativeItem().Text("Valor Líquido da OC:").Bold().FontSize(11).FontColor(Colors.Blue.Darken2);
                        r.ConstantItem(120).AlignRight().Text($"{net:N2}").Bold().FontSize(11).FontColor(Colors.Blue.Darken2);
                    });
                    col.Item().Text(t =>
                    {
                        t.Span("Valor Líquido da Ordem de Compra por Extenso:  ").Bold().FontSize(8);
                        t.Span(NumberToWordsPtBr.Currency(net) + "  " + new string('*', 30)).FontColor(Colors.Blue.Darken2).FontSize(8);
                    });

                    // aprovações do processo
                    if (quotation is not null)
                        col.Item().PaddingTop(4).BorderTop(0.5f).DefaultTextStyle(x => x.FontSize(7.5f)).Text(t =>
                        {
                            t.Span("Aprovações:  ").SemiBold();
                            t.Span($"Seleção: {quotation.SelectedByLabel} ({quotation.SelectedAt:dd/MM/yyyy})   ·   " +
                                   $"Gerência: {quotation.ManagerApprovedByLabel} ({quotation.ManagerApprovedAt:dd/MM/yyyy})   ·   " +
                                   // sem Nível 2 (AlcadaDoComprador) o documento diz isso, em vez de
                                   // imprimir "Diretoria:  ()" e parecer assinatura que faltou coletar
                                   (quotation.DirectorApprovedAt is null
                                       ? "Diretoria: dispensada (compra do Gestor de Suprimentos)"
                                       : $"Diretoria: {quotation.DirectorApprovedByLabel} ({quotation.DirectorApprovedAt:dd/MM/yyyy})"));
                        });
                });

                page.Footer().Element(f => Footer(f, order));
            });

            // página 2 — política de pagamento a fornecedores
            if (!string.IsNullOrWhiteSpace(c.PaymentPolicy))
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(24);
                    page.DefaultTextStyle(t => t.FontSize(8.5f));
                    page.Header().Element(h => Header(h, c));
                    page.Content().Column(col =>
                    {
                        col.Spacing(8);
                        col.Item().AlignCenter().Text("POLÍTICA DE PAGAMENTO A FORNECEDORES").Bold().FontSize(10);
                        col.Item().Text(c.PaymentPolicy!).FontSize(8.5f);
                    });
                    page.Footer().Element(f => Footer(f, order));
                });
        });

        return doc.GeneratePdf();
    }

    private static void Header(IContainer container, CompanyProfile c)
    {
        container.BorderBottom(1.4f).PaddingBottom(4).Row(r =>
        {
            r.RelativeItem(8).Column(col =>
            {
                col.Item().Text(c.LegalName is { Length: > 0 } ? c.LegalName : "EMPRESA NÃO CONFIGURADA (Cadastros → Empresa)").Bold().FontSize(10);
                col.Item().Text($"{c.Address}{(c.District is not null ? " — " + c.District : "")}").FontSize(8);
                col.Item().Text($"{c.City} — {c.State}   CEP {c.Zip}").FontSize(8);
                col.Item().DefaultTextStyle(x => x.FontSize(8)).Text(t => { t.Span("CNPJ.......: ").SemiBold(); t.Span(c.TaxId); t.Span("      Inscr. Est.: ").SemiBold(); t.Span(c.StateRegistration ?? "—"); });
                col.Item().DefaultTextStyle(x => x.FontSize(8)).Text(t => { t.Span("Fone/Fax: ").SemiBold(); t.Span(c.Phone ?? "—"); t.Span("      E-mail: ").SemiBold(); t.Span(c.Email ?? "—"); });
            });
            r.RelativeItem(4).AlignRight().AlignMiddle().Text("GRUPO TRINO").ExtraBold().FontSize(17).FontColor(Colors.Grey.Darken3);
        });
    }

    private static void Footer(IContainer container, PurchaseOrder order)
    {
        container.BorderTop(1).PaddingTop(3).Row(r =>
        {
            r.RelativeItem().DefaultTextStyle(x => x.FontSize(8)).Text(t => { t.Span("Data da Emissão do relatório:  ").SemiBold(); t.Span($"{order.CreatedAt:dd/MM/yyyy}"); });
            r.RelativeItem().AlignRight().DefaultTextStyle(x => x.FontSize(8)).Text(t => { t.Span("Comprador:  ").SemiBold(); t.Span(order.IssuedByLabel); });
        });
    }

    private static string FormatTaxId(string? taxId)
    {
        // O.C. exige fornecedor homologado, e homologar exige CNPJ (SUP-ERR-013) —
        // mas o PDF não é lugar de estourar: sem documento, sai o travessão.
        var digits = taxId ?? "";
        return digits.Length == 14
            ? $"{digits[..2]}.{digits[2..5]}.{digits[5..8]}/{digits[8..12]}-{digits[12..]}"
            : digits.Length == 11
                ? $"{digits[..3]}.{digits[3..6]}.{digits[6..9]}-{digits[9..]}"
                : digits.Length == 0 ? "—" : digits;
    }
}

/// <summary>Valor por extenso em pt-BR (reais e centavos) para o rodapé da OC.</summary>
public static class NumberToWordsPtBr
{
    private static readonly string[] Units =
        ["", "um", "dois", "três", "quatro", "cinco", "seis", "sete", "oito", "nove",
         "dez", "onze", "doze", "treze", "quatorze", "quinze", "dezesseis", "dezessete", "dezoito", "dezenove"];
    private static readonly string[] Tens =
        ["", "", "vinte", "trinta", "quarenta", "cinquenta", "sessenta", "setenta", "oitenta", "noventa"];
    private static readonly string[] Hundreds =
        ["", "cento", "duzentos", "trezentos", "quatrocentos", "quinhentos", "seiscentos", "setecentos", "oitocentos", "novecentos"];

    public static string Currency(decimal value)
    {
        if (value < 0) value = -value;
        var reais = (long)decimal.Truncate(value);
        var centavos = (int)decimal.Round((value - reais) * 100);
        if (centavos == 100) { reais += 1; centavos = 0; }

        var parts = new List<string>();
        if (reais > 0)
            parts.Add($"{Spell(reais)} {(reais == 1 ? "real" : "reais")}");
        if (centavos > 0)
            parts.Add($"{Spell(centavos)} {(centavos == 1 ? "centavo" : "centavos")}");
        if (parts.Count == 0) return "zero reais";
        return string.Join(" e ", parts);
    }

    public static string Spell(long n)
    {
        if (n == 0) return "zero";
        if (n == 100) return "cem";
        var groups = new (long div, string singular, string plural)[]
        {
            (1_000_000_000, "um bilhão", "bilhões"),
            (1_000_000, "um milhão", "milhões"),
            (1_000, "mil", "mil"),
        };
        var parts = new List<string>();
        foreach (var (div, singular, plural) in groups)
        {
            var g = n / div;
            if (g == 0) continue;
            n %= div;
            if (div == 1_000) parts.Add(g == 1 ? "mil" : $"{SpellBelowThousand(g)} mil");
            else parts.Add(g == 1 ? singular : $"{SpellBelowThousand(g)} {plural}");
        }
        if (n > 0) parts.Add(SpellBelowThousand(n));
        return string.Join(" e ", parts);
    }

    private static string SpellBelowThousand(long n)
    {
        var parts = new List<string>();
        if (n >= 100)
        {
            if (n == 100) return "cem";
            parts.Add(Hundreds[n / 100]);
            n %= 100;
        }
        if (n >= 20)
        {
            if (n % 10 == 0) parts.Add(Tens[n / 10]);
            else parts.Add($"{Tens[n / 10]} e {Units[n % 10]}");
        }
        else if (n > 0) parts.Add(Units[n]);
        return string.Join(" e ", parts);
    }
}
