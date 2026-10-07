using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TrinoSupply.Foundation.Api.Domain;
using static TrinoSupply.Foundation.Api.Analytics.RelatorioExecutivoPdf;

namespace TrinoSupply.Foundation.Api.Analytics;

/// <summary>O recorte escrito no cabeçalho do PDF: o que o filtro escolheu, em palavras.</summary>
public record RecorteDoRelatorioDeMaterial(string? CentroCusto, string? Familia, string? Produto, string? Solicitante);

/// <summary>
/// O PDF do relatório de solicitações de material: a lâmina com os números do período, os
/// blocos por centro, família, produto e solicitante, o mês a mês, o prazo por família e, como
/// anexo, <b>todas</b> as solicitações do período com valores. Nenhum número nasce aqui — a folha
/// escreve o que <see cref="AnalyticsDeMaterialService.MaterialAsync"/> devolveu, com os mesmos
/// helpers de página do relatório executivo, para a folha de material não destoar da de compras.
/// </summary>
public static class RelatorioDeMaterialPdf
{
    private static readonly System.Globalization.CultureInfo PtBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    public static byte[] Generate(RelatorioDeMaterial r, RecorteDoRelatorioDeMaterial recorte, CompanyProfile? company,
        string geradoPor, DateTimeOffset emitidoEm, byte[]? logo = null)
    {
        var culturaAnterior = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = PtBr;
        try { return Gerar(r, recorte, company ?? new CompanyProfile(), geradoPor, emitidoEm, logo); }
        finally { System.Globalization.CultureInfo.CurrentCulture = culturaAnterior; }
    }

    private static string Valor(decimal? v) => v is null ? "—" : Moeda(v.Value);
    private static string Qtd(decimal v) => v.ToString("0.##");
    private static string Data(DateTimeOffset? d) => d is null ? "—" : d.Value.UtcDateTime.ToString("dd/MM/yyyy");

    private static byte[] Gerar(RelatorioDeMaterial r, RecorteDoRelatorioDeMaterial recorte, CompanyProfile c,
        string geradoPor, DateTimeOffset emitidoEm, byte[]? logo)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var k = r.Kpis;

        var doc = Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(18);
            page.DefaultTextStyle(t => t.FontSize(7.5f).FontColor(Slate900));

            page.Header().Element(h => Cabecalho(h, c, r, recorte, geradoPor, emitidoEm, logo));
            page.Footer().Element(f => Rodape(f, geradoPor, emitidoEm));

            page.Content().PaddingTop(6).Column(col =>
            {
                col.Spacing(9);

                // ---- a lâmina: os números do período --------------------------------
                col.Item().Row(row =>
                {
                    row.Spacing(6);
                    Card(row, Blue, "SOLICITAÇÕES NO PERÍODO", k.Requested.ToString(),
                        $"{Qtd(k.RequestedQty)} itens pedidos · {k.Rejected} recusada(s) · {k.Cancelled} cancelada(s)",
                        k.RequestedPrev > 0 ? $"antes: {k.RequestedPrev}" : null);
                    Card(row, Emerald, "ATENDIDAS", k.Fulfilled.ToString(),
                        $"{k.Partial} parcial(is) · {k.PurchaseRouteItems} item(ns) foram para compra", null);
                    Card(row, Amber, "PENDENTES HOJE", (k.AwaitingApproval + k.InWarehouseQueue).ToString(),
                        $"{k.AwaitingApproval} aguardando o centro · {k.InWarehouseQueue} na fila do estoque", null);
                    Card(row, k.SlaMetPercent is >= 90 ? Emerald : k.SlaMetPercent is >= 70 ? Amber : Rose,
                        "ATENDIDAS NO PRAZO", Pct(k.SlaMetPercent),
                        $"{k.SlaMeasured} atendimento(s) medido(s) · {k.SlaBreachedOpen} na fila com prazo estourado", null);
                });
                col.Item().Row(row =>
                {
                    row.Spacing(6);
                    Card(row, Navy, "VALOR PEDIDO", Valor(k.RequestedValue),
                        "o que os centros pediram, pelo custo de compra no dia do pedido", null);
                    Card(row, Navy, "VALOR LIBERADO", Valor(k.ApprovedValue),
                        "o que o Nível 1 do centro liberou (o pedido, enquanto não decide)", null);
                    Card(row, Navy, "VALOR ENTREGUE", Valor(k.DeliveredValue),
                        "o que saiu do almoxarifado nas atendidas do período", null);
                    Card(row, k.ItemsWithoutPrice > 0 ? Rose : Emerald, "ITENS SEM CUSTO", k.ItemsWithoutPrice.ToString(),
                        k.ItemsWithoutPrice > 0
                            ? "fora dos valores acima: o produto ainda não tem custo de compra"
                            : "todos os itens do período têm custo cadastrado", null);
                });

                // ---- os blocos --------------------------------------------------------
                col.Item().Row(row =>
                {
                    row.Spacing(9);
                    row.RelativeItem().Element(e => Secao(e, "Por centro de custo", Blue, s => Ranking(s, r.ByCostCenter)));
                    row.RelativeItem().Element(e => Secao(e, "Por família", Blue, s => Ranking(s, r.ByFamily)));
                });
                col.Item().Row(row =>
                {
                    row.Spacing(9);
                    row.RelativeItem().Element(e => Secao(e, "Por produto", Emerald, s => Ranking(s, r.ByProduct)));
                    row.RelativeItem().Element(e => Secao(e, "Por solicitante", Emerald, s => Ranking(s, r.ByRequester)));
                });
                col.Item().Row(row =>
                {
                    row.Spacing(9);
                    row.RelativeItem().Element(e => Secao(e, "Mês a mês", Navy, s => Meses(s, r)));
                    row.RelativeItem().Element(e => Secao(e, "Prazo de atendimento por família", Amber, s => Prazos(s, r)));
                });

                // ---- o anexo: todas as solicitações do período ----------------------
                if (r.Requisitions is { } lista)
                {
                    col.Item().PageBreak();
                    col.Item().Text("ANEXO — TODAS AS SOLICITAÇÕES DO PERÍODO").Bold().FontSize(11).FontColor(Navy);
                    col.Item().Text($"{lista.Count} solicitação(ões) criada(s) no período, da mais recente para a mais antiga. "
                        + "Os valores são pelo custo de compra congelado no dia do pedido; \"sem custo\" é item sem custo cadastrado, não zero.")
                        .FontSize(7).Italic().FontColor(Slate);
                    if (lista.Count == 0) col.Item().Text("Nenhuma solicitação no período.").FontSize(8);
                    else col.Item().Table(t => Lista(t, lista));
                }
            });
        }));

        using var ms = new MemoryStream();
        doc.GeneratePdf(ms);
        return ms.ToArray();
    }

    private static void Ranking(ColumnDescriptor col, IReadOnlyList<LinhaDeMaterial> linhas)
    {
        if (linhas.Count == 0) { col.Item().Text("nada no período").FontSize(6.5f).Italic().FontColor(Slate); return; }
        col.Item().Table(t =>
        {
            Colunas(t, [null, 28, 36, 36, 48, 48]);
            Cabecalhos(t, ["", "Solic.", "Qtd pedida", "Qtd entregue", "Valor pedido", "Valor entregue"]);
            foreach (var l in linhas)
            {
                Celula(t, l.Label);
                CelulaNum(t, l.Count.ToString());
                CelulaNum(t, Qtd(l.Qty));
                CelulaNum(t, Qtd(l.Delivered));
                CelulaNum(t, Valor(l.RequestedValue));
                CelulaNum(t, Valor(l.DeliveredValue));
            }
        });
    }

    private static void Meses(ColumnDescriptor col, RelatorioDeMaterial r)
    {
        if (!r.Months.Any(m => m.Requested + m.Fulfilled + m.Rejected > 0))
        { col.Item().Text("nada no período").FontSize(6.5f).Italic().FontColor(Slate); return; }
        col.Item().Table(t =>
        {
            Colunas(t, [null, 34, 34, 34, 48, 48]);
            Cabecalhos(t, ["Mês", "Solic.", "Atend.", "Recus.", "Valor pedido", "Valor entregue"]);
            foreach (var m in r.Months)
            {
                Celula(t, m.Month);
                CelulaNum(t, m.Requested.ToString());
                CelulaNum(t, m.Fulfilled.ToString());
                CelulaNum(t, m.Rejected.ToString());
                CelulaNum(t, Valor(m.RequestedValue));
                CelulaNum(t, Valor(m.DeliveredValue));
            }
        });
    }

    private static void Prazos(ColumnDescriptor col, RelatorioDeMaterial r)
    {
        if (r.SlaByFamily.Count == 0) { col.Item().Text("sem atendimento medido no período").FontSize(6.5f).Italic().FontColor(Slate); return; }
        col.Item().Table(t =>
        {
            Colunas(t, [null, 40, 40, 40, 44]);
            Cabecalhos(t, ["Família", "Prazo (dias)", "Medidas", "No prazo", "Média (dias)"]);
            foreach (var p in r.SlaByFamily)
            {
                Celula(t, p.Family);
                CelulaNum(t, p.MaxDays?.ToString() ?? "—");
                CelulaNum(t, p.Measured.ToString());
                CelulaNum(t, p.MaxDays is null ? "—" : $"{p.Met} ({(p.Measured == 0 ? 0 : p.Met * 100 / p.Measured)}%)");
                CelulaNum(t, p.AvgDays is { } d ? d.ToString("0.#") : "—");
            }
        });
    }

    private static void Lista(TableDescriptor t, IReadOnlyList<SolicitacaoDoRelatorioDeMaterial> lista)
    {
        Colunas(t, [62, 42, null, 70, 66, 24, 34, 50, 50, 56]);
        Cabecalhos(t, ["Número", "Data", "Centro de custo", "Solicitante", "Situação", "Itens", "Qtd", "Valor pedido", "Valor entregue", "Prazo"]);
        foreach (var s in lista)
        {
            Celula(t, s.Number);
            Celula(t, Data(s.CreatedAt));
            Celula(t, s.CostCenterName);
            Celula(t, s.Requester);
            Celula(t, s.PurchaseRequisitionNumber is null ? s.StatusLabel : $"{s.StatusLabel} · {s.PurchaseRequisitionNumber}");
            CelulaNum(t, s.Items.ToString());
            CelulaNum(t, Qtd(s.RequestedQty));
            CelulaNum(t, s.ItemsWithoutPrice > 0 ? $"{Valor(s.RequestedValue)} ({s.ItemsWithoutPrice} sem custo)" : Valor(s.RequestedValue));
            CelulaNum(t, Valor(s.DeliveredValue));
            Celula(t, s.SlaStatus switch
            {
                "ESTOURADO" => $"estourado ({s.SlaDays}d de {s.SlaMaxDays}d)",
                "ATENCAO" => $"a vencer ({s.SlaDays}d de {s.SlaMaxDays}d)",
                "OK" => $"no prazo ({s.SlaDays}d de {s.SlaMaxDays}d)",
                _ => "—",
            });
        }
    }

    private static void Cabecalho(IContainer container, CompanyProfile c, RelatorioDeMaterial r,
        RecorteDoRelatorioDeMaterial recorte, string geradoPor, DateTimeOffset emitidoEm, byte[]? logo) =>
        container.BorderBottom(1.2f).BorderColor(Navy).PaddingBottom(5).Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem(8).Column(e =>
                {
                    e.Item().Text("GRUPO TRINO · SUPRIMENTOS").FontSize(6.5f).SemiBold().FontColor(Slate).LetterSpacing(0.08f);
                    e.Item().PaddingTop(1).Text("RELATÓRIO DE SOLICITAÇÕES DE MATERIAL").Bold().FontSize(13).FontColor(Navy);
                    e.Item().Text(c.LegalName is { Length: > 0 } ? c.LegalName : "Grupo Trino").FontSize(7.5f).FontColor(Slate);
                });
                if (logo is { Length: > 0 })
                    row.RelativeItem(4).AlignRight().AlignMiddle().Height(30).Image(logo).FitHeight();
                else
                    row.RelativeItem(4).AlignRight().AlignMiddle().Text("TRINO SUPPLY").ExtraBold().FontSize(14).FontColor(Navy);
            });
            col.Item().PaddingTop(5).Row(row =>
            {
                row.Spacing(4);
                Chip(row, "Período", $"{r.From:dd/MM/yyyy} a {r.To:dd/MM/yyyy}");
                Chip(row, "Centro de custo", recorte.CentroCusto ?? "todos");
                Chip(row, "Família", recorte.Familia ?? "todas");
                Chip(row, "Produto", recorte.Produto ?? "todos");
                Chip(row, "Solicitante", recorte.Solicitante ?? "todos");
                row.RelativeItem().AlignRight().AlignMiddle()
                    .Text($"extraído em {emitidoEm.UtcDateTime:dd/MM/yyyy HH:mm} UTC · por {geradoPor}")
                    .FontSize(6.2f).FontColor(Slate);
            });
        });

    private static void Rodape(IContainer container, string geradoPor, DateTimeOffset emitidoEm) =>
        container.BorderTop(0.6f).BorderColor(Slate200).PaddingTop(3).Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(6.5f).FontColor(Slate));
                t.Span("Trino Supply · relatório de solicitações de material · emitido em ");
                t.Span($"{emitidoEm.UtcDateTime:dd/MM/yyyy HH:mm} UTC por {geradoPor}");
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
