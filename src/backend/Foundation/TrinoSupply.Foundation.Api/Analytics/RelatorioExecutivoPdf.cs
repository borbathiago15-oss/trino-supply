using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TrinoSupply.Foundation.Api.Domain;

namespace TrinoSupply.Foundation.Api.Analytics;

/// <summary>
/// PDF do relatório executivo de suprimentos &amp; compras — a lâmina que a diretoria leva
/// para a reunião.
///
/// <para>
/// A primeira página é um <b>one-page executivo</b>: cabeçalho com o recorte em chips, cinco
/// números de impacto com a comparação contra o período anterior, e cinco leituras lado a
/// lado — geração de valor (as três réguas do saving e o ranking de compradores), origem da
/// demanda (centros, solicitantes e material × serviço), fornecedores e concorrências (curva
/// ABC, BIDs e vencedores), formas e prazos de pagamento, e governança (tempo do ciclo e
/// aderência à O.C. do ERP). As páginas seguintes são os anexos, com as tabelas completas de
/// cada bloco da tela — os mesmos números, para quem precisa do detalhe.
/// </para>
///
/// <para>
/// Mesmo modelo do PDF da O.C. (QuestPDF, A4), com uma diferença de propósito: aqui o
/// cabeçalho tem de dizer <b>o recorte</b>. Um relatório impresso sem o período e os filtros
/// aplicados vira número solto na mesa — e dois recortes diferentes viram a mesma folha.
/// </para>
/// </summary>
public static class RelatorioExecutivoPdf
{
    // paleta corporativa — a mesma do sistema (tailwind.config.ts), para a folha não destoar da tela
    private const string Navy = "#031430";
    private const string Blue = "#2563eb";
    private const string Emerald = "#047857";
    private const string Amber = "#b45309";
    private const string Rose = "#be123c";
    private const string Slate = "#64748b";
    private const string Slate900 = "#0f172a";
    private const string Slate100 = "#f1f5f9";
    private const string Slate200 = "#e2e8f0";

    private static readonly System.Globalization.CultureInfo PtBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    public static byte[] Generate(RelatorioExecutivo r, CompanyProfile? company, string geradoPor, byte[]? logo = null)
    {
        // A folha é lida em português: "78.700,00", não "78,700.00". O servidor roda em
        // cultura invariante, então a cultura é fixada aqui, só durante a geração.
        var culturaAnterior = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = PtBr;
        try { return Gerar(r, company, geradoPor, logo); }
        finally { System.Globalization.CultureInfo.CurrentCulture = culturaAnterior; }
    }

    private static byte[] Gerar(RelatorioExecutivo r, CompanyProfile? company, string geradoPor, byte[]? logo)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var c = company ?? new CompanyProfile();

        var doc = Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(18);
            page.DefaultTextStyle(t => t.FontSize(7.5f).FontColor(Slate900));

            page.Header().Element(h => Cabecalho(h, c, r, geradoPor, logo));
            page.Footer().Element(f => Rodape(f, r, geradoPor));

            page.Content().PaddingTop(6).Column(col =>
            {
                col.Spacing(9);

                // ---- página 1: a lâmina --------------------------------------------
                HeroCards(col, r);
                if (r.Coverage.OrdersWithoutPr > 0 || r.Coverage.Capped) Cobertura(col, r);

                col.Item().Row(row =>
                {
                    row.Spacing(9);
                    row.RelativeItem(6).Element(e => Secao(e, "Geração de valor — as três réguas do saving", Blue, s => Reguas(s, r)));
                    row.RelativeItem(5).Element(e => Secao(e, "Ranking de compradores", Blue, s => Compradores(s, r)));
                });

                col.Item().Row(row =>
                {
                    row.Spacing(9);
                    row.RelativeItem(5).Element(e => Secao(e, "Origem da demanda — centros de custo", Navy, s => Centros(s, r)));
                    row.RelativeItem(4).Element(e => Secao(e, "Quem solicitou", Navy, s => Solicitantes(s, r)));
                    row.RelativeItem(3).Element(e => Secao(e, "Materiais × serviços", Navy, s => Escopo(s, r)));
                });

                col.Item().Row(row =>
                {
                    row.Spacing(9);
                    row.RelativeItem(6).Element(e => Secao(e, "Fornecedores — curva ABC", Emerald, s => Abc(s, r)));
                    row.RelativeItem(5).Element(e => Secao(e, "Concorrências (BIDs) — quem ganhou", Emerald, s => Bids(s, r)));
                });

                col.Item().Row(row =>
                {
                    row.Spacing(9);
                    row.RelativeItem(6).Element(e => Secao(e, "Formas e prazos de pagamento", Amber, s => Pagamento(s, r)));
                    row.RelativeItem(5).Element(e => Secao(e, "Governança e eficiência", Rose, s => Governanca(s, r)));
                });

                // ---- anexos: as tabelas completas de cada bloco da tela ---------------
                col.Item().PageBreak();
                col.Item().Text("ANEXOS — DETALHAMENTO POR BLOCO").Bold().FontSize(11).FontColor(Navy);
                col.Item().Text("Os mesmos números da lâmina, abertos linha a linha. A numeração é a da tela do sistema.")
                    .FontSize(7).Italic().FontColor(Slate);

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

    // ======================================================================
    // lâmina
    // ======================================================================

    private static string Moeda(decimal v) => $"R$ {v:N2}";
    private static string Pct(double? v) => v is null ? "—" : $"{v:0.#}%";

    /// <summary>Os cinco números de impacto, cada um com a faixa da sua cor e o período anterior embaixo.</summary>
    private static void HeroCards(ColumnDescriptor col, RelatorioExecutivo r) =>
        col.Item().Row(row =>
        {
            row.Spacing(6);
            var a = r.Previous;
            var sv = r.SavingRulers;

            Card(row, Blue, "TOTAL TRANSACIONADO (SPEND)", Moeda(r.Kpis.Spend),
                $"{r.Kpis.Orders} pedido(s) · {r.Kpis.Suppliers} fornecedor(es)", VariacaoCurta(r.Kpis.Spend, a.Spend));
            Card(row, Emerald, "SAVING NEGOCIADO", Moeda(r.Kpis.SavingTotal),
                (r.Kpis.SavingPercent is { } p ? $"{p:0.#}% sobre a 1ª proposta · " : "")
                + $"concorrência {(sv.Competition.Processes > 0 ? Moeda(sv.Competition.Saving) : "n/a")} · "
                + $"orçamento {(sv.Budget.Processes > 0 ? Moeda(sv.Budget.Saving) : "n/a")}",
                VariacaoCurta(r.Kpis.SavingTotal, a.SavingTotal));
            Card(row, r.Kpis.OtifPercent is >= 90 ? Emerald : r.Kpis.OtifPercent is >= 70 ? Amber : Rose,
                "NÍVEL DE SERVIÇO (OTIF)", Pct(r.Kpis.OtifPercent),
                "entregas no prazo e completas", $"antes: {Pct(a.OtifPercent)}");
            Card(row, r.Kpis.UrgentPercent > 20 ? Rose : Amber, "ÍNDICE DE URGÊNCIAS", $"{r.Kpis.UrgentPercent:0.#}%",
                $"{r.Urgent.Orders} compra(s) emergenciais · {Moeda(r.Urgent.Value)}", $"antes: {a.UrgentPercent:0.#}%");
            Card(row, Navy, "PRAZO MÉDIO DE PAGAMENTO (DPO)",
                r.Payment.WeightedDays is { } d ? $"{d:0.#} dias" : "—",
                $"ponderado pelo valor · {r.Payment.OrdersWithDays} pedido(s) com prazo", null);
        });

    private static void Card(RowDescriptor row, string cor, string rotulo, string valor, string detalhe, string? anterior) =>
        row.RelativeItem().Border(0.6f).BorderColor(Slate200).Background(Colors.White).Column(k =>
        {
            k.Item().Height(3).Background(cor);
            k.Item().Padding(7).Column(b =>
            {
                b.Spacing(1.5f);
                b.Item().Text(rotulo).FontSize(6.4f).SemiBold().FontColor(Slate).LetterSpacing(0.05f);
                b.Item().Text(valor).Bold().FontSize(15).FontColor(Slate900);
                b.Item().Text(detalhe).FontSize(6.6f).FontColor(Slate);
                if (anterior is not null) b.Item().Text(anterior).FontSize(6.6f).SemiBold().FontColor(cor);
            });
        });

    private static string VariacaoCurta(decimal atual, decimal anterior)
    {
        if (anterior == 0) return $"antes: {Moeda(anterior)}";
        var pct = Math.Round((double)((atual - anterior) * 100 / anterior));
        var seta = pct > 0 ? "▲" : pct < 0 ? "▼" : "•";
        return $"{seta} {Math.Abs(pct):0}% vs período anterior ({Moeda(anterior)})";
    }

    /// <summary>Uma seção da lâmina: título com a cor da família, e o corpo num quadro branco.</summary>
    private static void Secao(IContainer c, string titulo, string cor, Action<ColumnDescriptor> corpo) =>
        c.Border(0.6f).BorderColor(Slate200).Background(Colors.White).Column(col =>
        {
            col.Item().BorderLeft(3).BorderColor(cor).PaddingLeft(5).PaddingVertical(3)
                .Text(titulo).Bold().FontSize(8.8f).FontColor(Slate900);
            col.Item().BorderTop(0.5f).BorderColor(Slate100).Padding(7).Column(corpo);
        });

    /// <summary>Barra proporcional: a fatia lida de relance sem virar gráfico.</summary>
    private static void Barra(IContainer c, double pct, string cor)
    {
        var p = Math.Max(0.5, Math.Min(100, pct));
        c.Height(4).Background(Slate100).Row(row =>
        {
            row.RelativeItem((float)p).Background(cor);
            if (p < 100) row.RelativeItem((float)(100 - p));
        });
    }

    /// <summary>Tabela compacta da lâmina.</summary>
    private static void Mini(ColumnDescriptor col, float?[] larguras, string[] cabecalhos, Action<TableDescriptor> linhas,
        string? vazio = null)
    {
        if (vazio is not null) { col.Item().Text(vazio).FontSize(6.5f).Italic().FontColor(Slate); return; }
        col.Item().Table(t =>
        {
            Colunas(t, larguras);
            t.Header(h =>
            {
                foreach (var titulo in cabecalhos)
                    h.Cell().BorderBottom(0.6f).BorderColor(Slate200).PaddingBottom(1.5f).PaddingRight(3)
                        .Text(titulo.ToUpperInvariant()).FontSize(6.2f).SemiBold().FontColor(Slate).LetterSpacing(0.04f);
            });
            linhas(t);
        });
    }

    private static void M(TableDescriptor t, string texto, string? cor = null, bool negrito = false)
    {
        var txt = t.Cell().BorderBottom(0.3f).BorderColor(Slate100).PaddingVertical(1.2f).PaddingRight(3)
            .Text(texto).FontSize(7.4f);
        if (cor is not null) txt.FontColor(cor);
        if (negrito) txt.SemiBold();
    }

    private static void N(TableDescriptor t, string texto, string? cor = null, bool negrito = false)
    {
        var txt = t.Cell().BorderBottom(0.3f).BorderColor(Slate100).PaddingVertical(1.2f).PaddingRight(3)
            .AlignRight().Text(texto).FontSize(7.4f);
        if (cor is not null) txt.FontColor(cor);
        if (negrito) txt.SemiBold();
    }

    private static void Reguas(ColumnDescriptor col, RelatorioExecutivo r)
    {
        var reguas = new (string Nome, string Contra, ReguaDoSaving Regua, string Cor)[]
        {
            ("Negociação", "1ª proposta × fechado", r.SavingRulers.Negotiation, Emerald),
            ("Concorrência (BIDs)", "maior proposta × vencedora", r.SavingRulers.Competition, Blue),
            ("Orçamento", "orçado na SC × O.C.", r.SavingRulers.Budget, Amber),
        };
        Mini(col, [null, 28, 48, 48, 48, 28, 30], ["Régua", "Proc.", "Base", "Fechado", "Saving", "%", ""], t =>
        {
            foreach (var (nome, contra, g, cor) in reguas)
            {
                t.Cell().BorderBottom(0.3f).BorderColor(Slate100).PaddingVertical(1.2f).PaddingRight(3).Column(x =>
                {
                    x.Item().Text(nome).FontSize(7.4f).SemiBold();
                    x.Item().Text(contra).FontSize(6).FontColor(Slate);
                });
                if (g.Processes == 0)
                {
                    N(t, "0");
                    t.Cell().ColumnSpan(5).BorderBottom(0.3f).BorderColor(Slate100).PaddingVertical(1.2f)
                        .Text("não se aplica a nenhum processo do recorte").FontSize(6.2f).Italic().FontColor(Slate);
                    continue;
                }
                N(t, $"{g.Processes}");
                N(t, Moeda(g.Baseline));
                N(t, Moeda(g.Closed));
                N(t, Moeda(g.Saving), g.Saving >= 0 ? Emerald : Rose, true);
                N(t, Pct(g.Percent), null, true);
                t.Cell().BorderBottom(0.3f).BorderColor(Slate100).PaddingVertical(3).PaddingRight(3)
                    .Element(e => Barra(e, g.Percent ?? 0, cor));
            }
        });
        col.Item().PaddingTop(2).Text("As três réguas respondem perguntas diferentes e não se somam. A barra é o % de cada uma (escala 0–100).")
            .FontSize(5.6f).Italic().FontColor(Slate);
    }

    private static void Compradores(ColumnDescriptor col, RelatorioExecutivo r) =>
        Mini(col, [null, 28, 58, 54, 30], ["Comprador", "Proc.", "Spend", "Saving", "%"], t =>
        {
            foreach (var b in r.Buyers.Take(6))
            {
                M(t, b.Buyer);
                N(t, $"{b.Processes}");
                N(t, Moeda(b.Spend));
                N(t, Moeda(b.Saving), b.Saving > 0 ? Emerald : null, b.Saving > 0);
                N(t, $"{b.SavingPercent:0.#}%");
            }
        }, r.Buyers.Count == 0 ? "Nenhum pedido no recorte." : null);

    private static void Centros(ColumnDescriptor col, RelatorioExecutivo r) =>
        Mini(col, [null, 26, 58, 44], ["Centro / gestor", "Ped.", "Valor", "%"], t =>
        {
            foreach (var cc in r.Demand.CostCenters)
            {
                t.Cell().BorderBottom(0.3f).BorderColor(Slate100).PaddingVertical(1.2f).PaddingRight(3).Column(x =>
                {
                    x.Item().Text($"{cc.Code} — {cc.Name}").FontSize(7.4f).SemiBold();
                    if (cc.Manager is not null) x.Item().Text($"gestor: {cc.Manager}").FontSize(6).FontColor(Slate);
                });
                N(t, $"{cc.Orders}");
                N(t, Moeda(cc.Value));
                t.Cell().BorderBottom(0.3f).BorderColor(Slate100).PaddingVertical(1.2f).PaddingRight(3).Column(x =>
                {
                    x.Item().Text($"{cc.Percent:0.#}%").FontSize(7.4f).AlignRight();
                    x.Item().Element(e => Barra(e, cc.Percent, Navy));
                });
            }
        }, r.Demand.CostCenters.Count == 0 ? "Nenhum pedido no recorte." : null);

    private static void Solicitantes(ColumnDescriptor col, RelatorioExecutivo r) =>
        Mini(col, [null, 26, 60], ["Solicitante", "SCs", "Comprado"], t =>
        {
            foreach (var s in r.Demand.Requesters)
            {
                M(t, s.Requester);
                N(t, $"{s.Requisitions}");
                N(t, Moeda(s.Value));
            }
        }, r.Demand.Requesters.Count == 0 ? "Nenhum pedido com solicitação de origem no recorte." : null);

    /// <summary>Rosca materiais × serviços em SVG: o único desenho que a tabela não substitui.</summary>
    private static void Escopo(ColumnDescriptor col, RelatorioExecutivo r)
    {
        var e = r.Demand.Scope;
        var total = e.Materials + e.Services;
        if (total <= 0) { col.Item().Text("Sem itens no recorte.").FontSize(6.5f).Italic().FontColor(Slate); return; }
        const double circ = 2 * Math.PI * 38;
        var materiais = circ * e.MaterialsPercent / 100;
        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
              <circle cx="50" cy="50" r="38" fill="none" stroke="{Amber}" stroke-width="13"/>
              <circle cx="50" cy="50" r="38" fill="none" stroke="{Blue}" stroke-width="13"
                stroke-dasharray="{materiais.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} {circ.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}"
                transform="rotate(-90 50 50)"/>
              <text x="50" y="47" text-anchor="middle" font-family="Helvetica, Arial, sans-serif" font-size="14" font-weight="700" fill="{Slate900}">{e.MaterialsPercent.ToString("0.#", PtBr)}%</text>
              <text x="50" y="60" text-anchor="middle" font-family="Helvetica, Arial, sans-serif" font-size="7" fill="{Slate}">materiais</text>
            </svg>
            """;
        col.Item().AlignCenter().Width(80).Svg(svg);
        col.Item().PaddingTop(3).Column(l =>
        {
            l.Spacing(1);
            Legenda(l, Blue, $"Materiais {e.MaterialsPercent:0.#}%", Moeda(e.Materials));
            Legenda(l, Amber, $"Serviços {e.ServicesPercent:0.#}%", Moeda(e.Services));
        });
        col.Item().PaddingTop(2).Text("Serviço = família SERVIÇOS ou cotação de serviço; o resto é fornecimento de materiais.")
            .FontSize(5.4f).Italic().FontColor(Slate);
    }

    private static void Legenda(ColumnDescriptor l, string cor, string rotulo, string valor) =>
        l.Item().Row(row =>
        {
            row.ConstantItem(7).AlignMiddle().Height(6).Background(cor);
            row.RelativeItem().PaddingLeft(3).Text(rotulo).FontSize(7).SemiBold();
            row.AutoItem().Text(valor).FontSize(7);
        });

    private static void Abc(ColumnDescriptor col, RelatorioExecutivo r)
    {
        var s = r.Suppliers;
        col.Item().PaddingBottom(3).Text(
                $"{s.SupplierCount} fornecedor(es) · maior fatia {Pct(s.Top1Percent)} · 3 maiores {Pct(s.Top3Percent)} · 5 maiores {Pct(s.Top5Percent)}")
            .FontSize(6.8f).FontColor(Slate);
        Mini(col, [null, 24, 58, 30, 34, 18], ["Fornecedor", "Ped.", "Spend", "%", "Acum.", "ABC"], t =>
        {
            foreach (var f in s.Rows.Take(8))
            {
                M(t, f.Supplier);
                N(t, $"{f.Orders}");
                N(t, Moeda(f.Value));
                N(t, $"{f.Percent:0.#}%");
                N(t, $"{f.Cumulative:0.#}%", Slate);
                var cor = f.Class == "A" ? Emerald : f.Class == "B" ? Amber : Slate;
                t.Cell().BorderBottom(0.3f).BorderColor(Slate100).PaddingVertical(1.2f).AlignCenter()
                    .Text(f.Class).FontSize(7.4f).Bold().FontColor(cor);
            }
        }, s.Rows.Count == 0 ? "Nenhum pedido no recorte." : null);
    }

    private static void Bids(ColumnDescriptor col, RelatorioExecutivo r)
    {
        var b = r.Bids;
        col.Item().PaddingBottom(4).Row(row =>
        {
            row.Spacing(4);
            Numero(row, $"{b.Processes}", "processos cotados");
            Numero(row, b.AverageProponents is { } m ? $"{m:0.#}" : "—", "proponentes por BID (média)");
            Numero(row, $"{b.WithCompetition}", "com dois ou mais proponentes");
        });
        Mini(col, [null, 34, 62], ["Vencedor de concorrência", "Vitórias", "Valor"], t =>
        {
            foreach (var v in b.Winners)
            {
                M(t, v.Supplier, null, true);
                N(t, $"{v.Wins}");
                N(t, Moeda(v.Value));
            }
        }, b.Winners.Count == 0 ? "Nenhum processo com disputa fechou no recorte." : null);
    }

    private static void Numero(RowDescriptor row, string valor, string rotulo) =>
        row.RelativeItem().Background(Slate100).Padding(4).Column(k =>
        {
            k.Item().Text(valor).Bold().FontSize(13).FontColor(Slate900);
            k.Item().Text(rotulo).FontSize(6.2f).FontColor(Slate);
        });

    private static void Pagamento(ColumnDescriptor col, RelatorioExecutivo r)
    {
        var p = r.Payment;
        col.Item().PaddingBottom(3).Text(p.WeightedDays is { } d
                ? $"DPO {d:0.#} dias, ponderado pelo valor de {p.OrdersWithDays} pedido(s) ({Moeda(p.ValueWithDays)}). O prazo vem da proposta vencedora; sem ela, da condição da O.C."
                : "Nenhum pedido do recorte tem prazo de pagamento legível.")
            .FontSize(6.8f).FontColor(Slate);
        Mini(col, [null, 30, 26, 58, 60], ["Condição comercial", "Dias", "Ped.", "Spend", "%"], t =>
        {
            foreach (var l in p.Terms.Take(7))
            {
                M(t, l.Term);
                N(t, l.Days is { } dd ? $"{dd} d" : "—", l.Days is null ? Slate : null);
                N(t, $"{l.Orders}");
                N(t, Moeda(l.Value));
                t.Cell().BorderBottom(0.3f).BorderColor(Slate100).PaddingVertical(1.2f).PaddingRight(3).Column(x =>
                {
                    x.Item().Text($"{l.Percent:0.#}%").FontSize(7.4f).AlignRight();
                    x.Item().Element(e => Barra(e, l.Percent, Amber));
                });
            }
        }, p.Terms.Count == 0 ? "Nenhum pedido no recorte." : null);
    }

    private static void Governanca(ColumnDescriptor col, RelatorioExecutivo r)
    {
        col.Item().Text("TEMPO DO CICLO (MEDIANA, EM DIAS)").FontSize(5.6f).SemiBold().FontColor(Slate).LetterSpacing(0.04f);
        col.Item().PaddingTop(2).PaddingBottom(4).Row(row =>
        {
            row.Spacing(3);
            foreach (var e in r.CycleTimes.Where(e => e.Stage != "solicitacao_oc"))
                row.RelativeItem().Background(Slate100).Padding(3).Column(k =>
                {
                    k.Item().Text(e.MedianDays is { } d ? $"{d:0.#} d" : "—").Bold().FontSize(10.5f).FontColor(Slate900);
                    k.Item().Text(EtapaCurta(e.Stage)).FontSize(5.8f).FontColor(Slate);
                });
            var total = r.CycleTimes.FirstOrDefault(e => e.Stage == "solicitacao_oc");
            if (total is not null)
                row.RelativeItem().Background(Navy).Padding(3).Column(k =>
                {
                    k.Item().Text(total.MedianDays is { } d ? $"{d:0.#} d" : "—").Bold().FontSize(10.5f).FontColor(Colors.White);
                    k.Item().Text("SC → O.C. total").FontSize(5.8f).FontColor(Colors.Grey.Lighten2);
                });
        });

        var ad = r.Adherence;
        var corAd = ad.Percent is >= 95 ? Emerald : ad.Percent is >= 80 ? Amber : Rose;
        col.Item().Row(row =>
        {
            row.Spacing(4);
            row.RelativeItem().Border(0.6f).BorderColor(corAd).Padding(4).Column(k =>
            {
                k.Item().Text("ADERÊNCIA AO FLUXO FORMAL DE O.C.").FontSize(5.4f).SemiBold().FontColor(Slate);
                k.Item().Text(Pct(ad.Percent)).Bold().FontSize(15).FontColor(corAd);
                k.Item().Text($"{ad.Formal} de {ad.Orders} pedido(s) com O.C. do ERP · {Moeda(ad.FormalValue)} de {Moeda(ad.Value)}")
                    .FontSize(5.4f).FontColor(Slate);
            });
            row.RelativeItem().Column(k =>
            {
                k.Spacing(2);
                k.Item().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(6.8f));
                    t.Span("Sem O.C. do ERP: ").SemiBold();
                    t.Span($"{r.WithoutErp.Orders} pela exceção justificada ({Moeda(r.WithoutErp.Value)})");
                    if (r.WithoutErp.ClosedWithoutReason > 0)
                        t.Span($" · {r.WithoutErp.ClosedWithoutReason} sem justificativa").FontColor(Rose).SemiBold();
                    t.Span($" · {r.WithoutErp.PendingOrders} na fila, por registrar.");
                });
                k.Item().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(6.8f));
                    t.Span("Saving de referência (× último preço pago): ").SemiBold();
                    t.Span($"ganho {Moeda(r.Reference.Gain)}").FontColor(Emerald);
                    t.Span(" · ");
                    t.Span($"perda {Moeda(r.Reference.Loss)}").FontColor(Rose);
                    t.Span($" · líquido {Moeda(r.Reference.Net)} em {r.Reference.Items} item(ns).");
                });
                k.Item().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(6.8f));
                    t.Span("Compliance: ").SemiBold();
                    t.Span($"{r.Kpis.Orders} pedido(s), {r.Urgent.Orders} urgente(s) ({r.Urgent.Percent:0.#}% do valor).");
                });
            });
        });
    }

    private static string EtapaCurta(string stage) => stage switch
    {
        "solicitacao_escolha" => "SC → escolha",
        "escolha_aprovacao" => "escolha → aprovação",
        "aprovacao_oc" => "aprovação → O.C.",
        "oc_recebimento" => "O.C. → recebimento",
        _ => stage,
    };

    // ======================================================================
    // anexos (tabelas completas) e moldura
    // ======================================================================

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

    private static void Cabecalho(IContainer container, CompanyProfile c, RelatorioExecutivo r, string geradoPor, byte[]? logo) =>
        container.BorderBottom(1.2f).BorderColor(Navy).PaddingBottom(5).Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem(8).Column(e =>
                {
                    e.Item().Text("GRUPO TRINO · SUPRIMENTOS").FontSize(6.5f).SemiBold().FontColor(Slate).LetterSpacing(0.08f);
                    e.Item().PaddingTop(1).Text("RELATÓRIO EXECUTIVO DE SUPRIMENTOS & COMPRAS").Bold().FontSize(13).FontColor(Navy);
                    e.Item().Text(c.LegalName is { Length: > 0 } ? c.LegalName : "Grupo Trino").FontSize(7.5f).FontColor(Slate);
                });
                // logotipo oficial, no canto direito; sem o arquivo, a marca sai em texto
                if (logo is { Length: > 0 })
                    row.RelativeItem(4).AlignRight().AlignMiddle().Height(30).Image(logo).FitHeight();
                else
                    row.RelativeItem(4).AlignRight().AlignMiddle().Text("TRINO SUPPLY").ExtraBold().FontSize(14).FontColor(Navy);
            });
            col.Item().PaddingTop(5).Row(row =>
            {
                row.Spacing(4);
                Chip(row, "Período", $"{r.From:dd/MM/yyyy} a {r.To:dd/MM/yyyy}");
                Chip(row, "Empresa", r.CompanyLabel ?? "todas");
                Chip(row, "Centro de custo", r.CostCenterLabel ?? "todos");
                Chip(row, "Comprador", r.BuyerLabel ?? "todos");
                row.RelativeItem().AlignRight().AlignMiddle()
                    .Text($"extraído em {r.GeneratedAt.UtcDateTime:dd/MM/yyyy HH:mm} UTC · por {geradoPor}")
                    .FontSize(6.2f).FontColor(Slate);
            });
        });

    private static void Chip(RowDescriptor row, string rotulo, string valor) =>
        row.AutoItem().Background(Slate100).Border(0.5f).BorderColor(Slate200).PaddingHorizontal(5).PaddingVertical(2).Text(t =>
        {
            t.Span(rotulo + ": ").FontSize(6.2f).FontColor(Slate);
            t.Span(valor).FontSize(6.6f).SemiBold().FontColor(Slate900);
        });

    private static void Rodape(IContainer container, RelatorioExecutivo r, string geradoPor) =>
        container.BorderTop(0.6f).BorderColor(Slate200).PaddingTop(3).Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(6.5f).FontColor(Slate));
                t.Span("Trino Supply · relatório executivo · emitido em ");
                t.Span($"{r.GeneratedAt.UtcDateTime:dd/MM/yyyy HH:mm} UTC por {geradoPor}");
            });
            row.RelativeItem().AlignRight().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(6.5f).FontColor(Slate));
                t.Span("Página ");
                t.CurrentPageNumber();
                t.Span(" de ");
                t.TotalPages();
            });
        });
}
