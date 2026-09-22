using System.Globalization;
using System.Text;
using TrinoSupply.Foundation.Api.Acoes;

namespace TrinoSupply.Foundation.Api.Melhoria;

public record LeituraDoIndicador(
    string? Nome, decimal? Baseline, decimal? Meta, decimal? Atual, string? Unidade,
    string Sentido, bool? AtingiuNoNumero, string Frase);

public record LeituraDoPrazo(
    DateOnly? Prazo, int? DiasRestantes, int? PercentualConsumido, bool Vencido, string Frase);

public record LeituraDasAcoes(
    int Total, int Abertas, int Atrasadas, int Concluidas, int Suspensas, string Frase);

public record CausaComAcoes(string Rotulo, bool Vital, int Acoes, string? Detalhe);

/// <param name="Chave">Identidade do sinal, para a tela escolher o ícone.</param>
/// <param name="Severidade">info | atencao | risco.</param>
public record SinalDoCiclo(string Chave, string Severidade, string Texto);

public record LeituraDoCiclo(
    LeituraDoIndicador Indicador, LeituraDoPrazo Prazo, LeituraDasAcoes Acoes,
    IReadOnlyList<CausaComAcoes> Causas, IReadOnlyList<SinalDoCiclo> Sinais,
    string ProximoPasso, IReadOnlyList<string> Frases);

/// <summary>
/// A leitura automática do ciclo — o diferencial do módulo, e não enfeite.
///
/// <para>
/// É <b>uma função</b>, e tela, A3 e qualquer relatório leem dela. Ela <b>não inventa nada</b>:
/// só diz em português o que os campos já afirmam. Onde falta dado ela diz que falta, em vez
/// de preencher com zero — "0% de avanço" e "ninguém mediu ainda" são notícias diferentes.
/// </para>
/// </summary>
public static class MotorDeLeitura
{
    /// <summary>A partir daqui o prazo entra como sinal: avisar no dia do vencimento é tarde.</summary>
    public const int DiasDeAtencao = 7;

    public static LeituraDoCiclo Ler(
        ImprovementCycle c, IReadOnlyList<AnaliseDeCausa> analises,
        IReadOnlyList<ActionItem> acoes, DateOnly hoje)
    {
        var indicador = DoIndicador(c);
        var prazo = DoPrazo(c, hoje);
        var contas = DasAcoes(acoes, hoje);
        var causas = CausasComAcoes(analises, acoes);
        var sinais = Sinais(c, analises, causas, acoes, prazo, hoje);

        var frases = new List<string> { indicador.Frase, prazo.Frase, contas.Frase };
        frases.RemoveAll(string.IsNullOrWhiteSpace);

        return new(indicador, prazo, contas, causas, sinais,
            ProximoPasso(c, sinais, contas), frases);
    }

    // ---- indicador -----------------------------------------------------------

    /// <summary>
    /// O sentido sai da própria meta: meta abaixo da linha de base é "reduzir", acima é
    /// "aumentar". Sem os dois números não há sentido nenhum a deduzir, e a leitura diz isso.
    /// </summary>
    private static LeituraDoIndicador DoIndicador(ImprovementCycle c)
    {
        var sentido = (c.Baseline, c.GoalValue) switch
        {
            ({ } b, { } m) when m < b => "reduzir",
            ({ } b, { } m) when m > b => "aumentar",
            ({ }, { }) => "manter",
            _ => "indefinido",
        };

        bool? atingiu = (c.ResultValue, c.GoalValue) switch
        {
            ({ } r, { } m) when sentido == "reduzir" => r <= m,
            ({ } r, { } m) when sentido == "aumentar" => r >= m,
            ({ } r, { } m) => r == m,
            _ => null,
        };

        var nome = string.IsNullOrWhiteSpace(c.Indicator) ? null : c.Indicator.Trim();
        var u = string.IsNullOrWhiteSpace(c.Unit) ? "" : " " + c.Unit.Trim();

        var frase = (nome, c.Baseline, c.GoalValue) switch
        {
            (null, _, _) => "Sem indicador declarado — a meta não tem como ser verificada em número.",
            (_, { } b, { } m) when c.ResultValue is { } r =>
                $"{nome}: de {N(b)}{u} para {N(m)}{u}; medido {N(r)}{u} — "
                + (atingiu == true ? "a meta foi cruzada no número." : "ainda não cruzou a meta."),
            (_, { } b, { } m) => $"{nome}: de {N(b)}{u} para {N(m)}{u}; ainda sem medição.",
            _ => $"{nome}: falta a linha de base ou a meta em número.",
        };

        return new(nome, c.Baseline, c.GoalValue, c.ResultValue,
            string.IsNullOrWhiteSpace(c.Unit) ? null : c.Unit.Trim(), sentido, atingiu, frase);
    }

    // ---- prazo ---------------------------------------------------------------

    private static LeituraDoPrazo DoPrazo(ImprovementCycle c, DateOnly hoje)
    {
        var prazo = c.GoalDeadline ?? c.EndDate;
        if (prazo is not { } p)
            return new(null, null, null, false, "Sem prazo de meta declarado.");

        var restantes = p.DayNumber - hoje.DayNumber;
        var inicio = c.StartDate ?? DateOnly.FromDateTime(c.CreatedAt.UtcDateTime);
        int? consumido = p.DayNumber > inicio.DayNumber
            ? Math.Clamp((int)Math.Round(
                (hoje.DayNumber - inicio.DayNumber) * 100d / (p.DayNumber - inicio.DayNumber)), 0, 100)
            : null;

        // prazo vencido é informação, nunca veredito: quem encerra o ciclo é uma pessoa
        var frase = restantes switch
        {
            < 0 => $"Meta vencida há {-restantes} dia(s) — o prazo não encerra o ciclo, quem encerra é você.",
            0 => "A meta vence hoje.",
            _ => $"Faltam {restantes} dia(s) para a meta"
                 + (consumido is { } pc ? $" — {pc}% do tempo já corrido." : "."),
        };
        return new(p, restantes, consumido, restantes < 0, frase);
    }

    // ---- ações ---------------------------------------------------------------

    /// <summary>
    /// Suspensa é contada à parte e <b>nunca</b> como atrasada: ela está parada por decisão da
    /// gestão, e o relógio não corre contra quem foi mandado parar. A régua é a mesma do plano
    /// de ação — <c>PlanoDeAcao.Atrasada</c> —, e não uma segunda conta.
    /// </summary>
    private static LeituraDasAcoes DasAcoes(IReadOnlyList<ActionItem> acoes, DateOnly hoje)
    {
        var abertas = acoes.Count(PlanoDeAcao.Aberta);
        var atrasadas = acoes.Count(a => PlanoDeAcao.Atrasada(a, hoje));
        var concluidas = acoes.Count(a => a.Status == StatusDaAcao.Concluida);
        var suspensas = acoes.Count(a => a.Status == StatusDaAcao.Suspensa);

        var frase = acoes.Count == 0
            ? "Nenhuma ação ligada a este ciclo ainda."
            : $"{acoes.Count} ação(ões): {abertas} em aberto, {concluidas} concluída(s)"
              + (atrasadas > 0 ? $", {atrasadas} atrasada(s)" : "")
              + (suspensas > 0 ? $", {suspensas} suspensa(s) — suspensa não conta atraso" : "")
              + ".";
        return new(acoes.Count, abertas, atrasadas, concluidas, suspensas, frase);
    }

    // ---- causas x ações ------------------------------------------------------

    /// <summary>
    /// As causas de <b>todas</b> as ferramentas, com quantas ações atacam cada uma. A mesma
    /// causa levantada por duas ferramentas aparece uma vez: o Ishikawa costuma listar o que o
    /// Pareto depois prioriza, e contá-la duas vezes faria a folha parecer ter o dobro de
    /// frentes. Vital é quem foi eleito por <b>alguma</b> delas.
    /// </summary>
    private static List<CausaComAcoes> CausasComAcoes(
        IReadOnlyList<AnaliseDeCausa> analises, IReadOnlyList<ActionItem> acoes)
    {
        var apontadas = acoes.Where(a => !string.IsNullOrWhiteSpace(a.RootCauseRef))
            .Select(a => Chave(a.RootCauseRef!)).ToList();
        var saida = new List<CausaComAcoes>();
        var vistas = new Dictionary<string, int>();
        foreach (var causa in analises.SelectMany(a => a.Causas))
        {
            var chave = Chave(causa.Rotulo);
            if (vistas.TryGetValue(chave, out var onde))
            {
                // vital por uma ferramenta é vital: quem prioriza vence quem só levanta
                if (causa.Vital && !saida[onde].Vital) saida[onde] = saida[onde] with { Vital = true };
                continue;
            }
            vistas[chave] = saida.Count;
            saida.Add(new(causa.Rotulo, causa.Vital,
                apontadas.Count(x => x == chave), causa.Detalhe));
        }
        return saida;
    }

    /// <summary>
    /// O elo entre ação e causa é o texto da causa, comparado sem acento, sem caixa e sem
    /// pontuação: "Falta de treinamento" e "falta de treinamento." são a mesma coisa, e exigir
    /// igualdade exata faria o sinal "causa vital sem ação" acender com a ação já escrita.
    /// </summary>
    public static string Chave(string texto)
    {
        var sb = new StringBuilder();
        foreach (var ch in texto.Normalize(NormalizationForm.FormD))
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
        return sb.ToString();
    }

    // ---- sinais --------------------------------------------------------------

    private static List<SinalDoCiclo> Sinais(
        ImprovementCycle c, IReadOnlyList<AnaliseDeCausa> analises, IReadOnlyList<CausaComAcoes> causas,
        IReadOnlyList<ActionItem> acoes, LeituraDoPrazo prazo, DateOnly hoje)
    {
        var sinais = new List<SinalDoCiclo>();

        if (c.Phase == FaseDoCiclo.Check && c.ResultValue is null)
            sinais.Add(new("check-pendente", "atencao",
                "O ciclo está em Check e ninguém mediu o indicador."));

        foreach (var causa in causas.Where(x => x.Vital && x.Acoes == 0))
            sinais.Add(new("causa-vital-sem-acao", "risco",
                $"A causa vital \"{causa.Rotulo}\" não tem nenhuma ação atacando-a."));

        if (analises.Count == 0 && c.Phase != FaseDoCiclo.Plan)
            sinais.Add(new("sem-ferramenta", "atencao",
                "Nenhuma ferramenta de causa foi preenchida — a causa raiz ficou sem análise."));

        if (c.Phase != FaseDoCiclo.Encerrado && prazo.DiasRestantes is { } d)
        {
            if (d < 0) sinais.Add(new("prazo-vencido", "risco", prazo.Frase));
            else if (d <= DiasDeAtencao)
                sinais.Add(new("prazo-vencendo", "atencao", $"A meta vence em {d} dia(s)."));
        }

        if (acoes.Count(a => PlanoDeAcao.Atrasada(a, hoje)) is > 0 and var atrasadas)
            sinais.Add(new("acao-atrasada", "atencao",
                $"{atrasadas} ação(ões) do ciclo passaram do prazo."));

        // a contradição aparece em vez de ser escondida: fechar com pendência pode ser a
        // decisão certa, mas ela fica à vista de quem abrir o ciclo depois
        if (c.Phase == FaseDoCiclo.Encerrado && acoes.Any(PlanoDeAcao.Aberta))
            sinais.Add(new("encerrado-com-pendencia", "risco",
                $"Ciclo encerrado com {acoes.Count(PlanoDeAcao.Aberta)} ação(ões) ainda em aberto."));

        return sinais;
    }

    // ---- próximo passo -------------------------------------------------------

    private static string ProximoPasso(
        ImprovementCycle c, IReadOnlyList<SinalDoCiclo> sinais, LeituraDasAcoes acoes)
    {
        if (c.Phase == FaseDoCiclo.Encerrado)
            return sinais.Any(s => s.Chave == "encerrado-com-pendencia")
                ? "Ciclo encerrado. Resolva ou cancele as ações que ficaram em aberto."
                : "Ciclo encerrado — nada a fazer aqui.";

        if (sinais.FirstOrDefault(s => s.Chave == "causa-vital-sem-acao") is { } vital)
            return vital.Texto + " Crie a ação que a ataca.";

        return c.Phase switch
        {
            FaseDoCiclo.Plan when string.IsNullOrWhiteSpace(c.RootCause) =>
                "Preencha a ferramenta de causa e conclua a causa raiz.",
            FaseDoCiclo.Plan when c.GoalValue is null =>
                "Declare a meta em número — indicador, linha de base e alvo.",
            FaseDoCiclo.Plan => "Passe para Do e crie as ações que atacam a causa raiz.",
            FaseDoCiclo.Do when acoes.Total == 0 => "Crie as ações do ciclo, cada uma apontando a causa que ataca.",
            FaseDoCiclo.Do when acoes.Abertas > 0 => $"{acoes.Abertas} ação(ões) em aberto: toque o plano até o fim.",
            FaseDoCiclo.Do => "Todas as ações fecharam — passe para Check e meça o indicador.",
            FaseDoCiclo.Check when c.ResultValue is null => "Meça o indicador e registre o resultado.",
            FaseDoCiclo.Check => "Analise o resultado e passe para Act.",
            FaseDoCiclo.Act when string.IsNullOrWhiteSpace(c.Standardization) =>
                "Registre o que se padroniza para o ganho não se perder.",
            _ => "Encerre o ciclo com o veredito — a meta foi atingida ou não, e por quê.",
        };
    }

    private static string N(decimal v) =>
        v == Math.Truncate(v) ? ((long)v).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.##", CultureInfo.InvariantCulture);
}
