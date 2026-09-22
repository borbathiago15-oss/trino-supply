namespace TrinoSupply.Foundation.Api.Acoes;

/// <summary>
/// Situação do plano — <b>derivada</b>, nunca digitada. A precedência importa: cancelado
/// vence tudo, concluído vence atrasado, e atrasado vence "em andamento". Fosse outra ordem,
/// um plano cancelado apareceria como atrasado e a lista cobraria trabalho que ninguém mais
/// vai fazer.
/// </summary>
public static class SituacaoDoPlano
{
    public const string Pendente = "PENDENTE";
    public const string EmAndamento = "EM_ANDAMENTO";
    public const string Atrasado = "ATRASADO";
    public const string Concluido = "CONCLUIDO";
    public const string Cancelado = "CANCELADO";

    public static readonly string[] Todas = [Pendente, EmAndamento, Atrasado, Concluido, Cancelado];
}

/// <summary>
/// O ciclo de vida do plano, que é outra pergunta que a situação: a situação diz como o
/// trabalho está, o <see cref="Encerrado"/> diz que alguém decidiu parar de acompanhá-lo.
/// </summary>
public static class VidaDoPlano
{
    public const string Ativo = "ATIVO";
    public const string Encerrado = "ENCERRADO";
}

/// <summary>Os vocabulários do plano, iguais aos do Trino Intelligence.</summary>
public static class VocabularioDoPlano
{
    public static readonly string[] Areas =
        ["RH", "Operações", "Segurança", "Qualidade", "Manutenção", "Logística",
         "Comercial", "TI", "Suprimentos", "Outros"];

    public static readonly string[] Categorias =
        ["Melhoria Contínua", "Redução de Custo", "Segurança", "Qualidade",
         "Eficiência Operacional", "Tecnologia", "Pessoas", "Compliance", "Expansão", "Outro"];

    /// <summary>
    /// Alta / Média / Baixa — o vocabulário do plano, e <b>não</b> o da solicitação de compra
    /// (LOW/NORMAL/HIGH/URGENT). São escalas de coisas diferentes: a da SC mede urgência de
    /// atendimento, esta mede quanto o plano importa. Unificá-las faria uma das duas mentir.
    /// </summary>
    public static readonly string[] Prioridades = ["ALTA", "MEDIA", "BAIXA"];

    public static readonly string[] Graus = ["BAIXA", "MEDIA", "ALTA"];

    public static bool AreaConhecida(string a) => Areas.Contains(a, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// O <b>plano</b> de ação — o projeto, com as ações dentro dele.
///
/// <para>
/// Ele existe porque a ação sozinha não conta a história: "trocar o filme do palete" não diz
/// qual problema resolve, quanto custa, quem patrocina nem o que se aprendeu. O plano é onde
/// isso vive, e as ações são o <b>como</b>. É o desenho do Trino Intelligence, trazido para cá
/// porque é o que a operação já conhece.
/// </para>
///
/// <para>
/// Encerrar é decisão, e não consequência do progresso: um plano a 100% continua ativo até
/// alguém dizer que acabou, e um plano a 40% pode ser encerrado se a decisão for essa — com
/// quem encerrou, quando e a evidência do que ficou.
/// </para>
/// </summary>
public class ActionPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>AP-2026-001.</summary>
    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Centro de custo a que o plano pertence; nulo é plano da casa.</summary>
    public string? CostCenter { get; set; }

    /// <summary>Áreas afetadas, em CSV — a convenção de lista deste projeto.</summary>
    public string? Areas { get; set; }
    /// <summary>O texto livre quando "Outros" foi escolhido.</summary>
    public string? OtherArea { get; set; }

    public string Priority { get; set; } = "MEDIA";
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    /// <summary>0 a 100, informado à mão. O progresso dos itens é outra conta — ver <c>PlanoDeAcao</c>.</summary>
    public int Completion { get; set; }

    // ---- o porquê do plano ---------------------------------------------------
    /// <summary>O problema que motivou o plano.</summary>
    public string? Problem { get; set; }
    /// <summary>Por quê — a justificativa de negócio.</summary>
    public string? BusinessReason { get; set; }
    public string? Category { get; set; }
    public string? Sponsor { get; set; }
    public string? ManagerName { get; set; }
    public string? OperationalImpact { get; set; }
    public string? FinancialImpact { get; set; }
    public string? KpiAffected { get; set; }
    public string? TargetGoal { get; set; }
    public string Criticality { get; set; } = "MEDIA";
    public string Complexity { get; set; } = "MEDIA";

    // ---- dinheiro ------------------------------------------------------------
    public decimal RoiExpected { get; set; }
    public decimal SavingExpected { get; set; }
    public decimal SavingRealized { get; set; }
    public decimal InvestmentPlanned { get; set; }
    public decimal InvestmentActual { get; set; }

    // ---- cancelamento e encerramento -----------------------------------------
    public bool Cancelled { get; set; }
    public string? CancelReason { get; set; }
    public string Life { get; set; } = VidaDoPlano.Ativo;
    public Guid? ClosedById { get; set; }
    public string? ClosedByLabel { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    /// <summary>A evidência do que foi feito — é o que sobra quando o plano fecha.</summary>
    public string? EvidenceNote { get; set; }

    /// <summary>
    /// O ciclo de melhoria que originou o plano, quando houve um. É <b>aqui</b> que o ponteiro
    /// mora, e não na ação: no Trino Intelligence a análise de causa aponta para o plano, e é
    /// isso que faz "as ações do ciclo" querer dizer uma coisa só.
    /// </summary>
    public Guid? CycleId { get; set; }

    /// <summary>
    /// Chave de deduplicação dos planos abertos por gatilho automático. Vazia nos planos
    /// criados à mão. Sem ela, o mesmo gatilho abriria um plano por avaliação.
    /// </summary>
    public string? AutoKey { get; set; }

    public Guid CreatedBy { get; set; }
    public string CreatedByLabel { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;

    public List<ActionPlanResponsible> Responsibles { get; set; } = [];
    public List<ActionItem> Items { get; set; } = [];
    public List<PlanRisk> Risks { get; set; } = [];
    public List<PlanRootCause> RootCauses { get; set; } = [];
    public List<PlanLesson> Lessons { get; set; } = [];
}

/// <summary>
/// Um responsável pelo plano. São <b>vários</b>: um plano de verdade atravessa áreas, e
/// escolher um nome só obrigaria a inventar um dono para o trabalho dos outros.
/// </summary>
public class ActionPlanResponsible
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public Guid UserId { get; set; }
    public string UserLabel { get; set; } = string.Empty;
}

/// <summary>
/// Um risco do plano. A severidade é <b>probabilidade × impacto</b>, e não um campo: dois
/// riscos "altos" que significam coisas diferentes é o que faz a matriz perder a serventia.
/// </summary>
public class PlanRisk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Probability { get; set; } = "MEDIA";
    public string Impact { get; set; } = "MEDIA";
    public string? Mitigation { get; set; }
    public Guid? ResponsibleId { get; set; }
    public string? ResponsibleLabel { get; set; }
    public string Status { get; set; } = "ABERTO";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A causa raiz <b>do plano</b> — 5 Porquês ou 6M, preenchida no sistema.
///
/// <para>
/// Ela convive com a do ciclo de melhoria e não a substitui: nem todo plano nasce de um ciclo,
/// e o plano que nasce de uma reunião também precisa dizer que problema ataca.
/// </para>
/// </summary>
public class PlanRootCause
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    /// <summary>CINCO_PORQUES ou ISHIKAWA — os mesmos nomes de <c>FerramentaDeCausa</c>.</summary>
    public string Method { get; set; } = "CINCO_PORQUES";
    public string? ContentJson { get; set; }
    public string? MainCause { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// As lições do plano. Elas existem para o próximo plano não repetir o erro deste — e por isso
/// guardam o que <b>não</b> funcionou com o mesmo cuidado do que funcionou.
/// </summary>
public class PlanLesson
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public string? WhatWorked { get; set; }
    public string? WhatFailed { get; set; }
    public string? Lessons { get; set; }
    public string? BestPractice { get; set; }
    public string? NextSteps { get; set; }
    public string? Recommendation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
