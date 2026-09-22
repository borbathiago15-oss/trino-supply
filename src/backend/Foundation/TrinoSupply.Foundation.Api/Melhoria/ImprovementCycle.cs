namespace TrinoSupply.Foundation.Api.Melhoria;

/// <summary>
/// As quatro fases do ciclo, mais o encerramento.
///
/// <para>
/// <see cref="Encerrado"/> <b>não</b> é a quinta fase do trabalho: é o veredito. Por isso ele
/// não se alcança pela edição comum — tem operação própria, com quem decidiu e por quê. Um
/// clique na trilha de fases fechava o ciclo sem registrar nada, e era daí que vinha o ciclo
/// "encerrado" com ação atrasada em aberto.
/// </para>
/// </summary>
public static class FaseDoCiclo
{
    public const string Plan = "PLAN";
    public const string Do = "DO";
    public const string Check = "CHECK";
    public const string Act = "ACT";
    public const string Encerrado = "ENCERRADO";

    /// <summary>As fases que a edição comum pode escolher — <see cref="Encerrado"/> fica de fora.</summary>
    public static readonly string[] EmAndamento = [Plan, Do, Check, Act];
    public static readonly string[] Todas = [Plan, Do, Check, Act, Encerrado];

    public static string Rotulo(string fase) => fase switch
    {
        Plan => "Plan — planejar",
        Do => "Do — executar",
        Check => "Check — verificar",
        Act => "Act — agir",
        Encerrado => "Encerrado",
        _ => fase,
    };
}

/// <summary>
/// O alcance do ciclo. <see cref="Setor"/> existe por um motivo concreto: sem ele, o ciclo de
/// um setor só cabia em "gestão", que é o balaio de tudo que não tem centro de custo — e lá o
/// colega do mesmo setor não via o plano do próprio setor.
/// </summary>
public static class EscopoDoCiclo
{
    /// <summary>Um centro de custo específico.</summary>
    public const string Centro = "CENTRO";
    /// <summary>Vários centros.</summary>
    public const string Multi = "MULTI";
    /// <summary>Uma regional inteira.</summary>
    public const string Regional = "REGIONAL";
    /// <summary>Um setor da casa.</summary>
    public const string Setor = "SETOR";
    /// <summary>Corporativo, sem centro de custo. <b>Não é público</b> — ver a visibilidade.</summary>
    public const string Gestao = "GESTAO";

    public static readonly string[] Todos = [Centro, Multi, Regional, Setor, Gestao];

    public static string Rotulo(string escopo) => escopo switch
    {
        Centro => "Centro de custo",
        Multi => "Vários centros",
        Regional => "Regional",
        Setor => "Setor",
        Gestao => "Gestão / corporativo",
        _ => escopo,
    };
}

/// <summary>
/// Um ciclo de melhoria (PDCA).
///
/// <para>
/// O ciclo é onde se trata a <b>causa</b>; o plano de ação é onde se trata a <b>tarefa</b>. O
/// módulo existe para as duas coisas não virarem a mesma: as ações do ciclo são
/// <c>ActionItem</c> apontando para ele (<c>CycleId</c>), e não uma tabela paralela — a ação
/// que nasce de um ciclo é a mesma coisa que a que nasce de uma reunião, e duplicar o modelo
/// daria dois lugares para olhar o mesmo trabalho.
/// </para>
///
/// <para>
/// Ele nasce de um problema, percorre Plan → Do → Check → Act e morre com um <b>veredito dado
/// por uma pessoa</b>. Prazo vencido não encerra nada: é informação na tela, nunca decisão.
/// </para>
/// </summary>
public class ImprovementCycle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>PDCA-2026-001.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Uma frase, não um rótulo: "Reduzir avarias na doca 2", não "Avarias".</summary>
    public string Title { get; set; } = string.Empty;

    public string Scope { get; set; } = EscopoDoCiclo.Gestao;
    /// <summary>Nome da regional, quando o escopo é regional. A regional é texto no centro de custo, não cadastro.</summary>
    public string? Region { get; set; }
    /// <summary>Vale com <b>qualquer</b> escopo: o setor de TI abre ciclo de um centro de custo.</summary>
    public Guid? SectorId { get; set; }
    /// <summary>Áreas afetadas, lista livre separada por vírgula.</summary>
    public string? Areas { get; set; }
    public string Priority { get; set; } = "NORMAL";

    // ---- PLAN ----------------------------------------------------------------
    /// <summary>O que está errado: fato + impacto.</summary>
    public string? Problem { get; set; }
    public string? CurrentSituation { get; set; }
    /// <summary>Nome da ferramenta escolhida — ver <c>FerramentaDeCausa</c>.</summary>
    public string? ToolName { get; set; }
    /// <summary>A ferramenta <b>preenchida</b>, em JSON. Nunca descrita em texto livre.</summary>
    public string? ToolData { get; set; }
    public string? CauseAnalysis { get; set; }
    public string? RootCause { get; set; }
    public string? GoalDescription { get; set; }
    public string? Indicator { get; set; }
    public decimal? Baseline { get; set; }
    public decimal? GoalValue { get; set; }
    public string? Unit { get; set; }
    public DateOnly? GoalDeadline { get; set; }

    // ---- CHECK ---------------------------------------------------------------
    public DateOnly? CheckedOn { get; set; }
    public decimal? ResultValue { get; set; }
    public string? CheckAnalysis { get; set; }
    /// <summary>
    /// <b>Nulável de propósito</b>: nulo quer dizer "ainda não verificado". Ele é parte do
    /// veredito e é dito por quem conduziu — não se deduz do indicador, que pode nem ter sido
    /// medido.
    /// </summary>
    public bool? GoalMet { get; set; }

    // ---- ACT -----------------------------------------------------------------
    public string? Standardization { get; set; }
    public string? Lessons { get; set; }
    public bool NewCycle { get; set; }

    // ---- controle ------------------------------------------------------------
    public string Phase { get; set; } = FaseDoCiclo.Plan;
    public Guid? OwnerId { get; set; }
    public string? OwnerLabel { get; set; }
    public DateOnly? StartDate { get; set; }
    /// <summary>Previsão de conclusão.</summary>
    public DateOnly? EndDate { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedById { get; set; }
    public string? ClosedByLabel { get; set; }
    public string? ClosedReason { get; set; }

    public Guid CreatedBy { get; set; }
    public string CreatedByLabel { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;

    public List<CycleWatcher> Watchers { get; set; } = [];
    public List<CycleCostCenter> CostCenters { get; set; } = [];
}

/// <summary>
/// "Quem mais acompanha". Para quem não conduz nem é dono, <b>esta lista é o acesso</b> ao
/// ciclo — por isso só quem conduz a edita.
/// </summary>
public class CycleWatcher
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CycleId { get; set; }
    public Guid UserId { get; set; }
    public string UserLabel { get; set; } = string.Empty;
}

/// <summary>Os centros de custo que o ciclo trata — um ciclo pode tratar de vários.</summary>
public class CycleCostCenter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CycleId { get; set; }
    /// <summary>Código do centro, como a SC grava: é o que casa com <c>ActionItem.CostCenter</c>.</summary>
    public string CostCenter { get; set; } = string.Empty;
}
