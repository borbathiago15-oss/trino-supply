using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TrinoSupply.Foundation.Api.Acoes;

namespace TrinoSupply.Foundation.Api.Melhoria;

/// <summary>
/// O ciclo em <b>uma folha A3 paisagem</b> — o relatório A3 clássico, que é o formato em que
/// um PDCA se leva para a reunião.
///
/// <para>
/// A faixa de leitura vai no topo, à esquerda fica <b>o que se pensou</b> (Plan) e à direita
/// <b>o que se fez e o que deu</b> (Do, Check, Act). A divisão não é estética: é a pergunta
/// que o leitor faz ao pegar a folha — "qual era o raciocínio?" de um lado, "e no que deu?"
/// do outro.
/// </para>
///
/// <para>
/// Ele usa o <b>mesmo</b> <see cref="MotorDeLeitura"/> da tela, e não uma segunda leitura. Se
/// o papel dissesse uma coisa e a tela outra, a reunião inteira pararia para descobrir em qual
/// acreditar — e nenhuma das duas voltaria a ser levada a sério.
/// </para>
/// </summary>
public static class A3DoCiclo
{
    public static byte[] Gerar(CicloCompleto c, DateOnly hoje)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var ciclo = c.Ciclo;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A3.Landscape());
                page.Margin(20);
                page.DefaultTextStyle(t => t.FontSize(8.5f));

                page.Header().Element(h => Cabecalho(h, c));
                page.Content().PaddingTop(8).Row(r =>
                {
                    r.RelativeItem(1).PaddingRight(6).Element(e => Esquerda(e, c));
                    r.ConstantItem(1).LineVertical(0.8f);
                    r.RelativeItem(1).PaddingLeft(6).Element(e => Direita(e, c, hoje));
                });
                page.Footer().AlignRight().Text(t =>
                {
                    t.Span($"{ciclo.Code} · gerado em {DateTime.UtcNow:dd/MM/yyyy}").FontSize(7).Light();
                });
            });
        }).GeneratePdf();
    }

    // ---- cabeçalho e faixa de leitura ----------------------------------------

    private static void Cabecalho(IContainer c, CicloCompleto completo)
    {
        var ciclo = completo.Ciclo;
        var leitura = completo.Leitura;
        c.Column(col =>
        {
            col.Spacing(4);
            col.Item().Row(r =>
            {
                r.RelativeItem().Column(t =>
                {
                    t.Item().Text(ciclo.Title).Bold().FontSize(15);
                    t.Item().Text(x =>
                    {
                        x.Span($"{ciclo.Code}  ·  {EscopoDoCiclo.Rotulo(ciclo.Scope)}").FontSize(8);
                        if (completo.SetorNome is { } s) x.Span($"  ·  {s}").FontSize(8);
                        x.Span($"  ·  dono: {ciclo.OwnerLabel ?? "—"}").FontSize(8);
                        if (ciclo.Leader is { } lider) x.Span($"  ·  líder: {lider}").FontSize(8);
                        if (ciclo.Mentor is { } mentor) x.Span($"  ·  mentor: {mentor}").FontSize(8);
                    });
                });
                r.ConstantItem(150).AlignRight().Column(t =>
                {
                    t.Item().AlignRight().Text(FaseDoCiclo.Rotulo(ciclo.Phase)).Bold().FontSize(10);
                    // o veredito só aparece quando existe: nulo é "ainda não respondido", e
                    // imprimir "não atingida" aí seria inventar a resposta no papel
                    if (ciclo.GoalMet is { } atingida)
                        t.Item().AlignRight().Text(atingida ? "META ATINGIDA" : "META NÃO ATINGIDA")
                            .Bold().FontSize(9);
                });
            });

            // a faixa de leitura: o que a tela mostra, palavra por palavra
            col.Item().Background(Colors.Grey.Lighten4).Border(0.6f).Padding(6).Column(f =>
            {
                f.Spacing(2);
                f.Item().Text(leitura.ProximoPasso).Bold().FontSize(9);
                foreach (var frase in leitura.Frases) f.Item().Text(frase).FontSize(8);
                foreach (var sinal in leitura.Sinais)
                    f.Item().Text($"• {sinal.Texto}").FontSize(8)
                        .FontColor(sinal.Severidade == "risco" ? Colors.Red.Darken2 : Colors.Orange.Darken3);
            });
        });
    }

    // ---- esquerda: o que se pensou (Plan) ------------------------------------

    private static void Esquerda(IContainer c, CicloCompleto completo)
    {
        var ciclo = completo.Ciclo;
        c.Column(col =>
        {
            col.Spacing(6);
            Titulo(col, "PLAN — o que se pensou");
            Bloco(col, "Problema", ciclo.Problem);
            Bloco(col, "Situação atual", ciclo.CurrentSituation);

            // todas as ferramentas da folha, na ordem em que a análise as usou: obrigar a
            // escolher uma faria o A3 contar meia história
            foreach (var a in completo.Analises)
            {
                col.Item().Text(a.Nome).Bold().FontSize(9);
                if (a.Aviso is { } aviso) { col.Item().Text(aviso).FontSize(8).Italic(); continue; }
                if (a.Degraus.Count > 0) Degraus(col, a);
                if (a.Grupos.Count > 0) Grupos(col, a);
                if (a.Causas.Count > 0) Causas(col, a);
                if (a.Ideias.Count > 0)
                    foreach (var i in a.Ideias) col.Item().Text($"• {i}").FontSize(8);
                if (a.Linhas is { Count: > 0 } linhas) Plano5W2H(col, linhas);
                if (a.Antes is not null || a.Depois is not null) Kaizen(col, a);
                if (a.FluxoAtual is { Count: > 0 } || a.FluxoProposto is { Count: > 0 }) Fluxo(col, a);
            }

            Bloco(col, "Causa raiz", ciclo.RootCause);
            Bloco(col, "Meta", ciclo.GoalDescription);
            if (ciclo.Indicator is { } ind)
                col.Item().Text(x =>
                {
                    x.Span("Indicador: ").SemiBold().FontSize(8);
                    x.Span($"{ind} — de {N(ciclo.Baseline)} para {N(ciclo.GoalValue)} {ciclo.Unit}")
                        .FontSize(8);
                    if (ciclo.GoalDeadline is { } p) x.Span($" até {p:dd/MM/yyyy}").FontSize(8);
                });
        });
    }

    private static void Degraus(ColumnDescriptor col, AnaliseDeCausa a)
    {
        foreach (var d in a.Degraus)
            col.Item().PaddingLeft((d.Numero - 1) * 8).Text(x =>
            {
                x.Span($"{d.Pergunta} ").FontSize(8).Light();
                x.Span(d.Resposta).FontSize(8);
            });
    }

    private static void Plano5W2H(ColumnDescriptor col, IReadOnlyList<LinhaDoPlano> linhas)
    {
        foreach (var l in linhas)
            col.Item().Text(x =>
            {
                x.Span(l.OQue).SemiBold().FontSize(8);
                var resto = new[]
                {
                    l.Quem is null ? null : $"quem: {l.Quem}",
                    l.Quando is null ? null : $"quando: {l.Quando}",
                    l.Onde is null ? null : $"onde: {l.Onde}",
                    l.Como is null ? null : $"como: {l.Como}",
                    l.Quanto is null ? null : $"quanto: {l.Quanto}",
                }.Where(v => v is not null);
                if (resto.Any()) x.Span("  (" + string.Join(" · ", resto) + ")").FontSize(7.5f);
            });
    }

    private static void Kaizen(ColumnDescriptor col, AnaliseDeCausa a)
    {
        col.Item().Row(r =>
        {
            r.RelativeItem().Text(x => { x.Span("Antes: ").SemiBold().FontSize(8); x.Span(a.Antes ?? "—").FontSize(8); });
            r.RelativeItem().Text(x => { x.Span("Depois: ").SemiBold().FontSize(8); x.Span(a.Depois ?? "—").FontSize(8); });
        });
        if (a.Resultado is { } res)
            col.Item().Text(x => { x.Span("Resultado: ").SemiBold().FontSize(8); x.Span(res).FontSize(8); });
    }

    private static void Fluxo(ColumnDescriptor col, AnaliseDeCausa a)
    {
        col.Item().Row(r =>
        {
            r.RelativeItem().Text(x =>
            {
                x.Span("Fluxo atual: ").SemiBold().FontSize(8);
                x.Span(string.Join(" → ", a.FluxoAtual ?? [])).FontSize(8);
            });
            r.RelativeItem().Text(x =>
            {
                x.Span("Proposto: ").SemiBold().FontSize(8);
                x.Span(string.Join(" → ", a.FluxoProposto ?? [])).FontSize(8);
            });
        });
    }

    private static void Grupos(ColumnDescriptor col, AnaliseDeCausa a)
    {
        foreach (var g in a.Grupos.Where(g => g.Itens.Count > 0))
            col.Item().Text(x =>
            {
                x.Span($"{g.Rotulo}: ").SemiBold().FontSize(8);
                x.Span(string.Join("; ", g.Itens)).FontSize(8);
            });
    }

    /// <summary>
    /// As causas com o vital marcado. A marca vem da <b>normalização</b>, e não de uma conta
    /// feita aqui: a régua do Pareto e a do GUT vivem num lugar só.
    /// </summary>
    private static void Causas(ColumnDescriptor col, AnaliseDeCausa a)
    {
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(cd => { cd.RelativeColumn(6); cd.RelativeColumn(2); cd.RelativeColumn(2); });
            foreach (var causa in a.Causas)
            {
                t.Cell().PaddingVertical(1).Text(x =>
                {
                    x.Span(causa.Rotulo).FontSize(8);
                    if (causa.Vital) x.Span("  ★").FontSize(8).Bold();
                });
                t.Cell().AlignRight().Text(causa.Detalhe ?? N(causa.Valor)).FontSize(8);
                t.Cell().AlignRight().Text(
                    causa.Acumulado is { } ac ? $"acum. {ac.ToString("0.#", Pt)}%" : "").FontSize(8);
            }
        });
        if (a.Causas.Any(x => x.Vital))
            col.Item().Text("★ causa vital, eleita pela própria ferramenta").FontSize(7).Light();
    }

    // ---- direita: o que se fez e o que deu (Do / Check / Act) ----------------

    private static void Direita(IContainer c, CicloCompleto completo, DateOnly hoje)
    {
        var ciclo = completo.Ciclo;
        c.Column(col =>
        {
            col.Spacing(6);
            Titulo(col, "DO — o que se fez");
            if (completo.Acoes.Count == 0) col.Item().Text("Nenhuma ação ligada a este ciclo.").FontSize(8).Italic();
            else col.Item().Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(2); cd.RelativeColumn(6); cd.RelativeColumn(3); cd.RelativeColumn(2);
                });
                t.Header(h =>
                {
                    foreach (var titulo in new[] { "Nº", "Ação / causa que ataca", "Responsável", "Situação" })
                        h.Cell().BorderBottom(0.6f).PaddingBottom(2).Text(titulo).SemiBold().FontSize(8);
                });
                foreach (var a in completo.Acoes)
                {
                    t.Cell().PaddingVertical(1).Text(a.Number).FontSize(7.5f);
                    t.Cell().PaddingVertical(1).Text(x =>
                    {
                        x.Span(a.Title).FontSize(7.5f);
                        if (a.RootCauseRef is { } causa) x.Span($"  ({causa})").FontSize(7).Light();
                    });
                    t.Cell().PaddingVertical(1).Text(a.ResponsibleLabel).FontSize(7.5f);
                    t.Cell().PaddingVertical(1).Text(Situacao(a, hoje)).FontSize(7.5f);
                }
            });

            Titulo(col, "CHECK — o que deu");
            col.Item().Text(completo.Leitura.Indicador.Frase).FontSize(8);
            Bloco(col, "Análise do resultado", ciclo.CheckAnalysis);

            Titulo(col, "ACT — o que fica");
            Bloco(col, "Padronização", ciclo.Standardization);
            Bloco(col, "Lições", ciclo.Lessons);
            if (ciclo.NewCycle) col.Item().Text("Pede um novo ciclo.").FontSize(8).SemiBold();

            if (ciclo.Phase == FaseDoCiclo.Encerrado)
            {
                Titulo(col, "VEREDITO");
                col.Item().Text($"Encerrado por {ciclo.ClosedByLabel} em {ciclo.ClosedAt:dd/MM/yyyy}.")
                    .FontSize(8).SemiBold();
                Bloco(col, "Motivo", ciclo.ClosedReason);
            }
        });
    }

    /// <summary>
    /// A situação como a linha da tela a diz — inclusive a regra que mais importa: a ação
    /// <b>suspensa</b> nunca aparece como atrasada, porque está parada por decisão.
    /// </summary>
    private static string Situacao(ActionItem a, DateOnly hoje) =>
        PlanoDeAcao.Atrasada(a, hoje) ? "Atrasada" : a.Status;

    // ---- apoios --------------------------------------------------------------

    private static void Titulo(ColumnDescriptor col, string texto) =>
        col.Item().BorderBottom(0.8f).PaddingBottom(2).Text(texto).Bold().FontSize(9.5f);

    private static void Bloco(ColumnDescriptor col, string rotulo, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return;
        col.Item().Text(x =>
        {
            x.Span($"{rotulo}: ").SemiBold().FontSize(8);
            x.Span(valor.Trim()).FontSize(8);
        });
    }

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    private static string N(decimal? v) =>
        v is null ? "—" : v == Math.Truncate(v.Value) ? ((long)v).ToString(Pt) : v.Value.ToString("0.##", Pt);
}
