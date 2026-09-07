using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TrinoSupply.Foundation.Api.Domain;

namespace TrinoSupply.Foundation.Api.Analytics;

/// <summary>
/// PDF do relatório executivo de compras — o que a diretoria leva para a reunião.
///
/// Segue o mesmo modelo do PDF da O.C. (QuestPDF, A4, cabeçalho da empresa), com uma
/// diferença de propósito: aqui o cabeçalho tem de dizer **o recorte**. Um relatório
/// impresso sem o período e os filtros aplicados vira número solto na mesa — e dois
/// recortes diferentes viram a mesma folha.
/// </summary>
public static class RelatorioExecutivoPdf
{
    public static byte[] Generate(RelatorioExecutivo r, CompanyProfile? company, string geradoPor)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var c = company ?? new CompanyProfile();

        var doc = Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(24);
            page.DefaultTextStyle(t => t.FontSize(8));

            page.Header().Element(h => Cabecalho(h, c, r));
            page.Footer().Element(f => Rodape(f, r, geradoPor));

            page.Content().PaddingTop(6).Column(col =>
            {
                col.Spacing(9);

                Kpis(col, r);
                if (r.Coverage.OrdersWithoutPr > 0 || r.Coverage.Capped) Cobertura(col, r);

                // 1 — o que foi comprado
                Bloco(col, "1. Compras por família", "O que foi comprado no período, pelo valor dos itens da O.C.",
                    t =>
                    {
                        Colunas(t, [null, 70, 70, 46, 50]);
                        Cabecalhos(t, ["Família", "Valor", "Quantidade", "Pedidos", "% do total"]);
                        foreach (var f in r.Families.Take(30))
                        {
                            Celula(t, f.Family);
                            CelulaNum(t, $"{f.Value:N2}");
                            CelulaNum(t, $"{f.Quantity:0.##}");
                            CelulaNum(t, $"{f.Orders}");
                            CelulaNum(t, $"{f.Percent:0.#}%");
                        }
                    }, r.Families.Count == 0 ? "Nenhuma compra no recorte." : null);

                // 2 — saving
                Bloco(col, "2. Saving por comprador",
                    "Ganho de negociação apurado contra a primeira proposta do fornecedor vencedor. " +
                    "Cada processo entra uma vez, mesmo quando a compra foi dividida em várias O.C.s.",
                    t =>
                    {
                        Colunas(t, [null, 46, 68, 68, 68, 44, 70]);
                        Cabecalhos(t, ["Comprador", "Proc.", "Base (1ª proposta)", "Fechado", "Saving", "%", "Spend"]);
                        foreach (var b in r.Buyers.Take(30))
                        {
                            Celula(t, b.Buyer);
                            CelulaNum(t, $"{b.Processes}");
                            CelulaNum(t, $"{b.Baseline:N2}");
                            CelulaNum(t, $"{b.Closed:N2}");
                            CelulaNum(t, $"{b.Saving:N2}");
                            CelulaNum(t, $"{b.SavingPercent:0.#}%");
                            CelulaNum(t, $"{b.Spend:N2}");
                        }
                    }, r.Buyers.Count == 0 ? "Nenhum pedido no recorte." : null);

                // 3 — concentração
                Bloco(col, "3. Concentração por fornecedor",
                    $"{r.Suppliers.SupplierCount} fornecedor(es) no recorte · maior fatia {Pct(r.Suppliers.Top1Percent)} · " +
                    $"3 maiores {Pct(r.Suppliers.Top3Percent)} · 5 maiores {Pct(r.Suppliers.Top5Percent)}",
                    t =>
                    {
                        Colunas(t, [null, 46, 80, 60]);
                        Cabecalhos(t, ["Fornecedor", "Pedidos", "Valor", "% do total"]);
                        foreach (var f in r.Suppliers.Rows.Take(30))
                        {
                            Celula(t, f.Supplier);
                            CelulaNum(t, $"{f.Orders}");
                            CelulaNum(t, $"{f.Value:N2}");
                            CelulaNum(t, $"{f.Percent:0.#}%");
                        }
                    }, r.Suppliers.Rows.Count == 0 ? "Nenhum pedido no recorte." : null);

                // 4 — urgência
                Bloco(col, "4. Peso das compras urgentes",
                    $"{r.Urgent.Orders} pedido(s) vindos de solicitação urgente — {r.Urgent.Value:N2} " +
                    $"({r.Urgent.Percent:0.#}% do período). Urgência exige motivo e impacto na SC.",
                    t =>
                    {
                        Colunas(t, [78, 74, null, 70, null]);
                        Cabecalhos(t, ["Pedido", "SC", "Fornecedor", "Valor", "Motivo declarado"]);
                        foreach (var u in r.Urgent.Items)
                        {
                            Celula(t, u.Number);
                            Celula(t, u.PrNumber ?? "—");
                            Celula(t, u.Supplier);
                            CelulaNum(t, $"{u.Value:N2}");
                            Celula(t, u.Reason ?? "—");
                        }
                    }, r.Urgent.Orders == 0 ? "Nenhuma compra urgente no recorte." : null);

                // 5 — OTIF
                Bloco(col, "5. Entrega no prazo (OTIF) por fornecedor",
                    "Só entram entregas encerradas com data prometida registrada. OTIF = no prazo E completo.",
                    t =>
                    {
                        Colunas(t, [null, 64, 60, 60, 56]);
                        Cabecalhos(t, ["Fornecedor", "Entregas medidas", "No prazo", "Completo", "OTIF"]);
                        foreach (var o in r.Otif.Take(30))
                        {
                            Celula(t, o.Supplier);
                            CelulaNum(t, $"{o.Measured}");
                            CelulaNum(t, Pct(o.OnTimePercent));
                            CelulaNum(t, Pct(o.InFullPercent));
                            CelulaNum(t, Pct(o.OtifPercent));
                        }
                    }, r.Otif.Count == 0 ? "Nenhuma entrega encerrada com data prometida no recorte." : null);

                // 6 — sem O.C. do ERP
                Bloco(col, "6. Compras sem O.C. do ERP",
                    $"{r.WithoutErp.Orders} compra(s) fechada(s) pela exceção — {r.WithoutErp.Value:N2} " +
                    $"({r.WithoutErp.Percent:0.#}% do período). A regra é a O.C. do SENIOR; a justificativa " +
                    "abaixo é a única exceção que libera o fechamento. " +
                    $"Outros {r.WithoutErp.PendingOrders} pedido(s) ({r.WithoutErp.PendingValue:N2}) seguem " +
                    "em aberto com a O.C. por registrar — fila, não exceção" +
                    (r.WithoutErp.ClosedWithoutReason > 0
                        ? $"; e {r.WithoutErp.ClosedWithoutReason} andaram sem O.C. e sem justificativa nenhuma."
                        : "."),
                    t =>
                    {
                        Colunas(t, [78, null, 68, 58, null]);
                        Cabecalhos(t, ["Pedido", "Fornecedor", "Valor", "Data", "Justificativa"]);
                        foreach (var s in r.WithoutErp.Items)
                        {
                            Celula(t, s.Number);
                            Celula(t, s.Supplier);
                            CelulaNum(t, $"{s.Value:N2}");
                            Celula(t, $"{s.IssuedOn:dd/MM/yyyy}");
                            Celula(t, s.Reason ?? "SEM JUSTIFICATIVA REGISTRADA");
                        }
                    }, r.WithoutErp.Items.Count == 0 ? "Toda compra fechada no recorte tem O.C. do ERP." : null);
            });
        }));

        return doc.GeneratePdf();
    }

    private static string Pct(double? v) => v is null ? "—" : $"{v:0.#}%";

    private static void Kpis(ColumnDescriptor col, RelatorioExecutivo r) =>
        col.Item().Border(0.8f).Padding(6).Row(row =>
        {
            void Kpi(string rotulo, string valor)
            {
                row.RelativeItem().Column(k =>
                {
                    k.Item().Text(rotulo).FontSize(7).FontColor(Colors.Grey.Darken1);
                    k.Item().Text(valor).Bold().FontSize(10);
                });
            }
            Kpi("Total comprado", $"{r.Kpis.Spend:N2}");
            Kpi("Pedidos", $"{r.Kpis.Orders}");
            Kpi("Fornecedores", $"{r.Kpis.Suppliers}");
            Kpi("Saving negociado", $"{r.Kpis.SavingTotal:N2}{(r.Kpis.SavingPercent is { } p ? $"  ({p:0.#}%)" : "")}");
            Kpi("Urgentes", $"{r.Kpis.UrgentPercent:0.#}%");
            Kpi("OTIF", Pct(r.Kpis.OtifPercent));
            Kpi("Sem O.C. do ERP", $"{r.Kpis.WithoutErpValue:N2}");
        });

    /// <summary>
    /// O que o recorte não alcança. Sai impresso porque um total que não fecha com o
    /// financeiro precisa dizer por quê **na mesma folha** — não no rodapé de outra tela.
    /// </summary>
    private static void Cobertura(ColumnDescriptor col, RelatorioExecutivo r) =>
        col.Item().Background(Colors.Amber.Lighten5).Border(0.8f).BorderColor(Colors.Amber.Darken1)
            .Padding(5).Column(a =>
            {
                a.Item().Text("Cobertura deste recorte").SemiBold().FontSize(8);
                if (r.Coverage.OrdersWithoutPr > 0)
                    a.Item().Text($"{r.Coverage.OrdersWithoutPr} pedido(s), somando {r.Coverage.ValueWithoutPr:N2}, " +
                        "não têm solicitação de origem — logo não têm empresa nem centro de custo, e ficam de fora " +
                        "de qualquer filtro por esses dois campos.").FontSize(7.5f);
                if (r.Coverage.Capped)
                    a.Item().Text($"O período tem mais de {r.Coverage.Cap} pedidos: este relatório traz os " +
                        $"{r.Coverage.Cap} mais recentes. Reduza o período para fechar o total.").FontSize(7.5f);
            });

    private static void Bloco(ColumnDescriptor col, string titulo, string explicacao,
        Action<TableDescriptor> corpo, string? vazio)
    {
        col.Item().Column(b =>
        {
            b.Spacing(3);
            b.Item().Text(titulo).Bold().FontSize(10).FontColor(Colors.Blue.Darken2);
            b.Item().Text(explicacao).FontSize(7).Italic().FontColor(Colors.Grey.Darken2);
            if (vazio is not null) b.Item().PaddingTop(2).Text(vazio).FontSize(8);
            else b.Item().Table(corpo);
        });
    }

    private static void Colunas(TableDescriptor t, float?[] larguras) =>
        t.ColumnsDefinition(cd =>
        {
            foreach (var l in larguras)
            {
                if (l is null) cd.RelativeColumn();
                else cd.ConstantColumn(l.Value);
            }
        });

    private static void Cabecalhos(TableDescriptor t, string[] titulos) =>
        t.Header(h =>
        {
            foreach (var titulo in titulos)
                h.Cell().BorderBottom(1).PaddingVertical(2).PaddingRight(4)
                    .Text(titulo).SemiBold().FontSize(7.5f);
        });

    private static void Celula(TableDescriptor t, string texto) =>
        t.Cell().BorderBottom(0.3f).BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(1.5f).PaddingRight(4).Text(texto).FontSize(7.5f);

    private static void CelulaNum(TableDescriptor t, string texto) =>
        t.Cell().BorderBottom(0.3f).BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(1.5f).PaddingRight(4).AlignRight().Text(texto).FontSize(7.5f);

    private static void Cabecalho(IContainer container, CompanyProfile c, RelatorioExecutivo r) =>
        container.BorderBottom(1.4f).PaddingBottom(4).Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem(8).Column(e =>
                {
                    e.Item().Text(c.LegalName is { Length: > 0 } ? c.LegalName : "GRUPO TRINO").Bold().FontSize(10);
                    e.Item().Text("RELATÓRIO EXECUTIVO DE COMPRAS").SemiBold().FontSize(12);
                });
                row.RelativeItem(4).AlignRight().AlignMiddle()
                    .Text("GRUPO TRINO").ExtraBold().FontSize(15).FontColor(Colors.Grey.Darken3);
            });
            col.Item().PaddingTop(3).Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(8));
                t.Span("Período: ").SemiBold();
                t.Span($"{r.From:dd/MM/yyyy} a {r.To:dd/MM/yyyy}");
                t.Span("      Empresa: ").SemiBold();
                t.Span(r.CompanyLabel ?? "todas");
                t.Span("      Centro de custo: ").SemiBold();
                t.Span(r.CostCenterLabel ?? "todos");
                t.Span("      Comprador: ").SemiBold();
                t.Span(r.BuyerLabel ?? "todos");
            });
        });

    private static void Rodape(IContainer container, RelatorioExecutivo r, string geradoPor) =>
        container.BorderTop(1).PaddingTop(3).Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(7.5f));
                t.Span("Emitido em ").SemiBold();
                t.Span($"{r.GeneratedAt.UtcDateTime:dd/MM/yyyy HH:mm} UTC por {geradoPor}");
            });
            row.RelativeItem().AlignRight().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(7.5f));
                t.Span("Página ");
                t.CurrentPageNumber();
                t.Span(" de ");
                t.TotalPages();
            });
        });
}
