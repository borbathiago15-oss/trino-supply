using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace TrinoSupply.Api.Materials;

/// <summary>Linha da ficha de entrega já resolvida (descrição, código/CA, quantidade, data).</summary>
public sealed record FichaPdfLine(decimal Quantity, string Description, string ItemCode, string Group, string? Ca, DateTimeOffset DeliveredAt);

/// <summary>
/// Dados da Ficha de Entrega de EPI/Uniformes para renderização (spec Sistema de Almoxarifado).
/// Cabeçalho com empresa que entrega + colaborador; linhas com espaço para assinatura por item.
/// </summary>
public sealed record FichaPdfData(
    string CompanyName, string CompanyTaxId, string CostCenterCode, string CollaboratorName,
    string? Registration, DateOnly? AdmissionDate, string Reason, string IssuedBy, DateTimeOffset IssuedAt,
    IReadOnlyList<FichaPdfLine> Lines);

/// <summary>
/// Gera o PDF da <b>Ficha de Entrega de EPI's e/ou Uniformes</b> pré-preenchida a partir de uma baixa
/// de consumo, com termo de responsabilidade e uma linha por item entregue para coleta de assinatura.
/// </summary>
public static class FichaPdf
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    private static string Qty(decimal v) => v.ToString("0.######", Br);
    private static string Date(DateTimeOffset? d) => d?.ToString("dd/MM/yyyy", Br) ?? "";
    private static string Date(DateOnly? d) => d?.ToString("dd/MM/yyyy", Br) ?? "";

    private const string Termo =
        "Declaro que recebi da empresa, gratuitamente, os Equipamentos de Proteção Individual e/ou " +
        "uniformes abaixo relacionados e o treinamento quanto ao seu uso. Será de minha responsabilidade " +
        "utilizá-los apenas para a finalidade a que se destinam, zelar pela sua guarda e conservação e " +
        "devolvê-los ao setor competente quando impróprios para o uso ou por motivo de desligamento e/ou " +
        "afastamento, conforme C.L.T. art. 157 e 158 e Norma Regulamentadora NR-6.";

    public static byte[] Build(FichaPdfData f)
    {
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(t => t.FontSize(9).FontColor(Colors.Black));

                page.Header().Border(1).Padding(6).Column(col =>
                {
                    col.Item().AlignCenter().Text("FICHA DE ENTREGA DE E.P.I's E/OU UNIFORMES").Bold().FontSize(13);
                    col.Item().AlignCenter().Text("Segurança do Trabalho").FontSize(9).FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingTop(8).Column(col =>
                {
                    // ---- Cabeçalho: empresa que entrega + colaborador ----
                    col.Item().Border(1).Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                        void Field(string label, string value) => t.Cell().Border(0.5f).Padding(4).Text(txt =>
                        {
                            txt.Span($"{label}: ").FontColor(Colors.Grey.Darken1);
                            txt.Span(value).Bold();
                        });
                        Field("Empresa (entrega)", f.CompanyName);
                        Field("CNPJ", f.CompanyTaxId);
                        Field("Colaborador", f.CollaboratorName);
                        Field("Matrícula", f.Registration ?? "");
                        Field("Centro de custo", f.CostCenterCode);
                        Field("Data de contratação", Date(f.AdmissionDate));
                        Field("Motivo", f.Reason);
                        Field("Resp. entrega", f.IssuedBy);
                    });

                    // ---- Termo de responsabilidade ----
                    col.Item().PaddingTop(6).Border(1).Padding(6).Column(tc =>
                    {
                        tc.Item().Text("TERMO DE RESPONSABILIDADE").Bold().FontSize(9);
                        tc.Item().PaddingTop(2).Text(Termo).FontSize(8).Justify();
                    });

                    // ---- Itens entregues (uma assinatura por linha) ----
                    col.Item().PaddingTop(6).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(38);  // Qtde
                            c.RelativeColumn(3);   // Descrição
                            c.RelativeColumn(2);   // Código / Nº C.A
                            c.ConstantColumn(70);  // Dt. Entrega
                            c.RelativeColumn(2);   // Assinatura
                        });

                        void H(string s) => t.Cell().Background(Colors.Grey.Lighten2).Border(0.5f).Padding(4).Text(s).Bold().FontSize(8);
                        H("Qtde"); H("Descrição"); H("Código / Nº C.A"); H("Dt. Entrega"); H("Assinatura");

                        foreach (var l in f.Lines)
                        {
                            // Fardamento → código do item; EPI → número do C.A cadastrado.
                            var codeOrCa = string.Equals(l.Group, "EPI", StringComparison.OrdinalIgnoreCase)
                                ? (string.IsNullOrWhiteSpace(l.Ca) ? "C.A não cadastrado" : $"C.A {l.Ca}")
                                : l.ItemCode;
                            t.Cell().Border(0.5f).Padding(4).Text(Qty(l.Quantity));
                            t.Cell().Border(0.5f).Padding(4).Text(l.Description);
                            t.Cell().Border(0.5f).Padding(4).Text(codeOrCa);
                            t.Cell().Border(0.5f).Padding(4).Text(Date(l.DeliveredAt));
                            t.Cell().Border(0.5f).Padding(4).MinHeight(22).Text(""); // assinatura em branco
                        }
                    });

                    col.Item().PaddingTop(16).Row(r =>
                    {
                        r.RelativeItem().AlignCenter().Column(sc =>
                        {
                            sc.Item().PaddingTop(18).LineHorizontal(0.8f);
                            sc.Item().AlignCenter().Text("Assinatura do colaborador").FontSize(8);
                        });
                    });

                    col.Item().PaddingTop(10).Text(
                        "C.A — Certificado de Aprovação. Deixar de usar os EPI sem justificativa constitui falta grave " +
                        "(art. 482, CLT).").FontSize(7).FontColor(Colors.Grey.Darken1);
                });

                page.Footer().AlignRight().Text(t =>
                {
                    t.Span("Emitida em ").FontSize(7).FontColor(Colors.Grey.Darken1);
                    t.Span(Date(f.IssuedAt)).FontSize(7).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();
    }
}
