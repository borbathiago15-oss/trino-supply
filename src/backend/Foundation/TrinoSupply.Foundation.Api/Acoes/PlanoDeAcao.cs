namespace TrinoSupply.Foundation.Api.Acoes;

/// <summary>
/// As leituras de uma ação. Ficam num lugar só porque a lista, o painel e — mais tarde — a
/// leitura automática do ciclo de melhoria precisam responder igual: duas contas para
/// "está atrasada?" dariam um card que não bate com a lista que ele abre.
/// </summary>
public static class PlanoDeAcao
{
    /// <summary>
    /// Passou do prazo e ainda espera trabalho.
    ///
    /// <para>
    /// <b>Ação suspensa nunca está atrasada.</b> Ela está parada por decisão de quem manda, e
    /// o relógio não corre contra quem foi mandado parar. Contá-la como atraso transformaria
    /// uma decisão da gestão em falha da equipe — e é o tipo de número que faz o time perder
    /// a confiança no painel inteiro.
    /// </para>
    /// </summary>
    public static bool Atrasada(ActionItem a, DateOnly hoje) =>
        a.Status != StatusDaAcao.Suspensa
        && !StatusDaAcao.Encerrada(a.Status)
        && a.DueDate is { } prazo && prazo < hoje;

    /// <summary>Ainda espera trabalho: nem concluída, nem cancelada. Suspensa continua aberta.</summary>
    public static bool Aberta(ActionItem a) => !StatusDaAcao.Encerrada(a.Status);

    /// <summary>Dias de atraso, ou nulo quando não está atrasada.</summary>
    public static int? DiasDeAtraso(ActionItem a, DateOnly hoje) =>
        Atrasada(a, hoje) ? hoje.DayNumber - a.DueDate!.Value.DayNumber : null;

    /// <summary>
    /// O progresso que a ação de fato tem. Concluída é 100 e cancelada não conta: deixar o
    /// número digitado valer faria a lista mostrar "concluída, 40%", que não quer dizer nada.
    /// </summary>
    public static int ProgressoReal(ActionItem a) => a.Status switch
    {
        StatusDaAcao.Concluida => 100,
        StatusDaAcao.Cancelada => 0,
        _ => Math.Clamp(a.Progress, 0, 100),
    };

    /// <summary>
    /// Os números das ações de um plano, sobre o mesmo conjunto que a tela lista. A suspensa
    /// conta em coluna própria e <b>fora</b> do atraso — é a régua de <see cref="Atrasada"/>,
    /// e não uma segunda conta.
    /// </summary>
    public static PlacarDasAcoes Placar(IReadOnlyList<ActionItem> acoes, DateOnly hoje) => new(
        acoes.Count,
        acoes.Count(a => a.Status == StatusDaAcao.Pendente),
        acoes.Count(a => a.Status == StatusDaAcao.EmAndamento),
        acoes.Count(a => a.Status == StatusDaAcao.Concluida),
        acoes.Count(a => a.Status == StatusDaAcao.Suspensa),
        acoes.Count(a => a.Status == StatusDaAcao.Cancelada),
        acoes.Count(a => Atrasada(a, hoje)),
        acoes.Sum(a => a.ExpectedGain ?? 0),
        acoes.Sum(a => a.RealizedGain ?? 0));
}

/// <summary>
/// As leituras do <b>plano</b>. Como as da ação, elas moram num lugar só: a lista, o cartão do
/// topo e o relatório precisam responder igual, senão o card promete uma coisa e a lista abre
/// outra.
/// </summary>
public static class PlanoDoPlano
{
    /// <summary>
    /// A situação derivada, na ordem que importa: cancelado vence tudo, concluído vence
    /// atrasado, atrasado vence "em andamento". Fosse outra ordem, um plano cancelado
    /// apareceria como atrasado e a lista cobraria trabalho que ninguém mais vai fazer.
    /// </summary>
    public static string Situacao(ActionPlan p, DateOnly hoje)
    {
        if (p.Cancelled) return SituacaoDoPlano.Cancelado;
        if (p.Completion >= 100) return SituacaoDoPlano.Concluido;
        if (p.DueDate is { } prazo && prazo < hoje) return SituacaoDoPlano.Atrasado;
        return p.Completion > 0 ? SituacaoDoPlano.EmAndamento : SituacaoDoPlano.Pendente;
    }

    /// <summary>
    /// O progresso que os itens mostram — média simples do progresso real de cada um.
    ///
    /// <para>
    /// Ele <b>não</b> substitui o <c>Completion</c> digitado: os dois respondem perguntas
    /// diferentes — "quanto das minhas ações andou" e "quanto eu digo que o plano andou" —, e
    /// a divergência entre eles é informação, não erro. Plano sem item cai no digitado, que é
    /// a única resposta que existe.
    /// </para>
    /// </summary>
    public static int ProgressoDosItens(ActionPlan p) =>
        p.Items.Count == 0
            ? Math.Clamp(p.Completion, 0, 100)
            : (int)Math.Round(p.Items.Average(PlanoDeAcao.ProgressoReal));

    /// <summary>O ganho do plano mais o dos itens — o plano pode ter ganho que não é de item nenhum.</summary>
    public static decimal SavingEsperado(ActionPlan p) =>
        p.SavingExpected + p.Items.Sum(i => i.ExpectedGain ?? 0);

    public static decimal SavingRealizado(ActionPlan p) =>
        p.SavingRealized + p.Items.Sum(i => i.RealizedGain ?? 0);

    /// <summary>
    /// ROI em percentual: (ganho realizado − investimento) ÷ investimento. <b>Nulo sem
    /// investimento</b> — dividir por zero daria um número que parece ótimo e não quer dizer
    /// nada, e é o tipo de número que vai para a apresentação antes de alguém conferir.
    /// </summary>
    public static decimal? Roi(ActionPlan p)
    {
        var investido = p.InvestmentActual > 0 ? p.InvestmentActual : p.InvestmentPlanned;
        if (investido <= 0) return null;
        return Math.Round((SavingRealizado(p) - investido) / investido * 100m, 1);
    }

    /// <summary>Plano encerrado com ação em aberto — a contradição aparece, não se esconde.</summary>
    public static bool EncerradoComPendencia(ActionPlan p) =>
        p.Life == VidaDoPlano.Encerrado && p.Items.Any(PlanoDeAcao.Aberta);
}

/// <summary>
/// A severidade de um risco: <b>probabilidade × impacto</b>. Ela é calculada, e não escolhida
/// — dois riscos marcados "alto" que significam coisas diferentes é o que faz a matriz deixar
/// de servir para priorizar.
/// </summary>
public static class SeveridadeDoRisco
{
    private static readonly Dictionary<string, int> Peso = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BAIXA"] = 1, ["MEDIA"] = 2, ["ALTA"] = 3, ["MUITO_ALTA"] = 4,
    };

    public static readonly string[] Graus = ["BAIXA", "MEDIA", "ALTA", "MUITO_ALTA"];

    public static int Pontos(PlanRisk r) =>
        (Peso.TryGetValue(r.Probability ?? "", out var p) ? p : 2)
        * (Peso.TryGetValue(r.Impact ?? "", out var i) ? i : 2);

    public static string Rotulo(PlanRisk r) => Pontos(r) switch
    {
        >= 12 => "CRITICO",
        >= 6 => "ALTO",
        >= 3 => "MEDIO",
        _ => "BAIXO",
    };
}

/// <summary>Os números do topo da tela de ações.</summary>
public record PlacarDasAcoes(
    int Total, int Pendentes, int EmAndamento, int Concluidas, int Suspensas, int Canceladas,
    int Atrasadas, decimal GanhoEsperado, decimal GanhoRealizado);

/// <summary>Os números do topo da tela de planos.</summary>
public record PlacarDosPlanos(
    int Total, int Pendentes, int EmAndamento, int Atrasados, int Concluidos, int Cancelados,
    int Encerrados, int EncerradosComPendencia,
    decimal SavingEsperado, decimal SavingRealizado);
