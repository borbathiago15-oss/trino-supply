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
}

/// <summary>Os números do topo da tela de ações.</summary>
public record PlacarDasAcoes(
    int Total, int Pendentes, int EmAndamento, int Concluidas, int Suspensas, int Canceladas,
    int Atrasadas, decimal GanhoEsperado, decimal GanhoRealizado);
