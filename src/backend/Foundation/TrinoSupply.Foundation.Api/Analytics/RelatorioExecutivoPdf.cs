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

                // 3 — saving mês a mês
                Bloco(col, "3. Saving do período, mês a mês",
                    "O pedido conta no mês em que foi criado; o processo conta uma vez, no mês do primeiro pedido que o fechou. " +
                    "Mês sem pedido aparece zerado.",
                    t =>
                    {
                        Colunas(t, [null, 80, 50, 50, 80, 50]);
                        Cabecalhos(t, ["Mês", "Total comprado", "Pedidos", "Proc.", "Saving", "%"]);
                        foreach (var m in r.Months)
                        {
                            Celula(t, MesPorExtenso(m.Month));
                            CelulaNum(t, $"{m.Spend:N2}");
                            CelulaNum(t, $"{m.Orders}");
                            CelulaNum(t, $"{m.Processes}");
                            CelulaNum(t, $"{m.Saving:N2}");
                            CelulaNum(t, Pct(m.SavingPercent));
                        }
                    }, r.Months.Count == 0 ? "Nenhum mês no recorte." : null);

                // 4 — as três réguas
                Bloco(col, "4. As três réguas do saving",
                    "Negociação: contra a primeira proposta do vencedor. Concorrência: contra a maior proposta completa do BID " +
                    "(não se aplica com um proponente só). Orçamento: contra o que o solicitante informou na SC (só quando todas " +
                    "as SCs do processo informaram). Não se somam.",
                    t =>
                    {
                        Colunas(t, [null, 50, 80, 80, 80, 50]);
                        Cabecalhos(t, ["Régua", "Proc.", "Base", "Fechado", "Saving", "%"]);
                        foreach (var (nome, regua) in new[]
                                 {
                                     ("Negociação", r.SavingRulers.Negotiation),
                                     ("Concorrência", r.SavingRulers.Competition),
                                     ("Orçamento", r.SavingRulers.Budget),
                                 })
                        {
                            Celula(t, nome);
                            if (regua.Processes == 0)
                            {
                                CelulaNum(t, "0");
                                Celula(t, "não se aplica a nenhum processo do recorte");
                                Celula(t, ""); Celula(t, ""); Celula(t, "");
                                continue;
                            }
                            CelulaNum(t, $"{regua.Processes}");
                            CelulaNum(t, $"{regua.Baseline:N2}");
                            CelulaNum(t, $"{regua.Closed:N2}");
                            CelulaNum(t, $"{regua.Saving:N2}");
                            CelulaNum(t, Pct(regua.Percent));
                        }
                    }, null);

                // 5 — saving por família e por fornecedor
                Bloco(col, "5. Saving por família e por fornecedor",
                    "O saving é do processo. Quando o processo virou mais de uma O.C., ele é rateado entre elas pelo valor " +
                    "de cada uma e, dentro da O.C., entre as famílias pelo valor dos itens. Uma O.C. de uma família só sai exata.",
                    t =>
                    {
                        Colunas(t, [null, 40, 80, 80, 44]);
                        Cabecalhos(t, ["Família / Fornecedor", "Proc.", "Comprado", "Saving", "%"]);
                        foreach (var l in r.SavingByFamily.Take(20))
                        {
                            Celula(t, l.Label);
                            CelulaNum(t, $"{l.Processes}");
                            CelulaNum(t, $"{l.Spend:N2}");
                            CelulaNum(t, $"{l.Saving:N2}");
                            CelulaNum(t, Pct(l.SavingPercent));
                        }
                        foreach (var l in r.SavingBySupplier.Take(20))
                        {
                            Celula(t, "fornecedor · " + l.Label);
                            CelulaNum(t, $"{l.Processes}");
                            CelulaNum(t, $"{l.Spend:N2}");
                            CelulaNum(t, $"{l.Saving:N2}");
                            CelulaNum(t, Pct(l.SavingPercent));
                        }
                    }, r.SavingByFamily.Count == 0 ? "Nenhum processo com saving no recorte." : null);

                // 6 — saving de referência
                Bloco(col, "6. Saving de referência (× último preço pago)",
                    $"Preço fechado contra o último preço pago do mesmo produto de catálogo, congelado no registro da O.C. " +
                    $"{r.Reference.Items} item(ns) em {r.Reference.Orders} pedido(s): ganho {r.Reference.Gain:N2} · " +
                    $"perda {r.Reference.Loss:N2} · líquido {r.Reference.Net:N2}. A perda vem primeiro: é ela que pede ação.",
                    t =>
                    {
                        Colunas(t, [70, null, 40, 60, 60, 70]);
                        Cabecalhos(t, ["Pedido", "Produto / fornecedor", "Qtd", "Último pago", "Fechado", "Diferença"]);
                        foreach (var l in r.Reference.Rows)
                        {
                            Celula(t, l.Order);
                            Celula(t, $"{l.Description}{(l.CatalogCode is null ? "" : $" ({l.CatalogCode})")} · {l.Supplier}");
                            CelulaNum(t, $"{l.Quantity:0.##}");
                            CelulaNum(t, $"{l.LastPaidUnitPrice:N2}");
                            CelulaNum(t, $"{l.UnitPrice:N2}");
                            CelulaNum(t, $"{l.Saving:N2}");
                        }
                    }, r.Reference.Items == 0 ? "Nenhum item do recorte tem preço pago anterior para comparar." : null);

                // 7 — concentração
                Bloco(col, "7. Concentração por fornecedor",
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
                Bloco(col, "8. Peso das compras urgentes",
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
                Bloco(col, "9. Entrega no prazo (OTIF) por fornecedor",
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

                // 8 — tempo do ciclo
                Bloco(col, "10. Tempo do ciclo (mediana, em dias)",
                    "Cada etapa conta pelo seu próprio relógio e só entra quando as duas marcas existem. " +
                    "Mediana, não média: um processo parado por meses não esconde os outros que andaram em uma semana.",
                    t =>
                    {
                        Colunas(t, [null, 60, 70]);
                        Cabecalhos(t, ["Etapa", "Medidos", "Mediana"]);
                        foreach (var e in r.CycleTimes)
                        {
                            Celula(t, e.Title);
                            CelulaNum(t, $"{e.Measured}");
                            CelulaNum(t, e.MedianDays is { } d ? $"{d:0.#} d" : "—");
                        }
                    }, null);

                // 6 — sem O.C. do ERP
                Bloco(col, "11. Compras sem O.C. do ERP",
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
            // a linha de baixo é o período anterior, do mesmo tamanho: o número sem ela é solto
            void Kpi(string rotulo, string valor, string? anterior = null)
            {
                row.RelativeItem().Column(k =>
                {
                    k.Item().Text(rotulo).FontSize(7).FontColor(Colors.Grey.Darken1);
                    k.Item().Text(valor).Bold().FontSize(10);
                    if (anterior is not null) k.Item().Text(anterior).FontSize(6.5f).FontColor(Colors.Grey.Darken1);
                });
            }
            var a = r.Previous;
            Kpi("Total comprado", $"{r.Kpis.Spend:N2}", Variacao(r.Kpis.Spend, a.Spend));
            Kpi("Pedidos", $"{r.Kpis.Orders}", Variacao(r.Kpis.Orders, a.Orders));
            Kpi("Fornecedores", $"{r.Kpis.Suppliers}");
            Kpi("Saving negociado", $"{r.Kpis.SavingTotal:N2}{(r.Kpis.SavingPercent is { } p ? $"  ({p:0.#}%)" : "")}",
                Variacao(r.Kpis.SavingTotal, a.SavingTotal));
            Kpi("Urgentes", $"{r.Kpis.UrgentPercent:0.#}%", $"antes: {a.UrgentPercent:0.#}%");
            Kpi("OTIF", Pct(r.Kpis.OtifPercent), $"antes: {Pct(a.OtifPercent)}");
            Kpi("Sem O.C. do ERP", $"{r.Kpis.WithoutErpValue:N2}");
        });

    /// <summary>"antes: 10.000,00 (▲ 20%)" — o período anterior e a variação contra ele.</summary>
    private static string Variacao(decimal atual, decimal anterior)
    {
        var valor = $"{anterior:N2}";
        if (anterior == 0) return $"antes: {valor}";
        var pct = Math.Round((double)((atual - anterior) * 100 / anterior));
        var seta = pct > 0 ? "▲" : pct < 0 ? "▼" : "•";
        return $"antes: {valor} ({seta} {Math.Abs(pct):0}%)";
    }

    private static string MesPorExtenso(string yyyyMM)
    {
        var partes = yyyyMM.Split('-');
        string[] nomes = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];
        return partes.Length == 2 && int.TryParse(partes[1], out var m) && m is >= 1 and <= 12
            ? $"{nomes[m - 1]}/{partes[0][2..]}" : yyyyMM;
    }

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
