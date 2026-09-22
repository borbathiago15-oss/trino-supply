namespace TrinoSupply.Foundation.Api.Acoes;

/// <summary>
/// Situação de uma ação. <see cref="Suspensa"/> não é detalhe: a ação suspensa está parada
/// <b>por decisão</b>, então o prazo não corre contra ela. Contá-la como atrasada culparia a
/// equipe por uma decisão da gestão — ver <see cref="PlanoDeAcao.Atrasada"/>.
/// </summary>
public static class StatusDaAcao
{
    public const string Pendente = "PENDENTE";
    public const string EmAndamento = "EM_ANDAMENTO";
    public const string Concluida = "CONCLUIDA";
    public const string Suspensa = "SUSPENSA";
    public const string Cancelada = "CANCELADA";

    public static readonly string[] Todos =
        [Pendente, EmAndamento, Concluida, Suspensa, Cancelada];

    /// <summary>Encerrada: não espera mais trabalho, e por isso sai das filas de pendência.</summary>
    public static bool Encerrada(string status) => status is Concluida or Cancelada;
}

/// <summary>
/// Uma ação do plano — o 5W2H de uma coisa a fazer, com dono e prazo.
///
/// <para>
/// O responsável é <b>chave estrangeira</b>, e não texto. Com nome digitado à mão, dois
/// "João Silva" e um "J. Silva" viram três pessoas, e a pergunta que mais importa — "o que
/// está pendente com o João?" — deixa de ter resposta.
/// </para>
///
/// <para>
/// A ação vive por si: ela pode nascer de um ciclo de melhoria, de uma reunião ou de um
/// achado solto. Quem a originou é um ponteiro opcional, não a razão de ela existir.
/// </para>
/// </summary>
public class ActionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = string.Empty;          // AC-2026-000123

    // ---- 5W2H ----------------------------------------------------------------
    /// <summary>O quê.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Por quê — a razão de a ação existir.</summary>
    public string? Reason { get; set; }
    /// <summary>Onde — área ou local de apoio.</summary>
    public string? Area { get; set; }
    /// <summary>Quem: o dono da ação.</summary>
    public Guid ResponsibleId { get; set; }
    /// <summary>Nome de quem era o dono no momento — o histórico não muda se o cadastro mudar.</summary>
    public string ResponsibleLabel { get; set; } = string.Empty;
    /// <summary>Quando: começo previsto e prazo.</summary>
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    /// <summary>Quanto: ganho esperado e o realizado, quando houver.</summary>
    public decimal? ExpectedGain { get; set; }
    public decimal? RealizedGain { get; set; }

    public string? ExpectedResult { get; set; }
    public string? Kpi { get; set; }

    /// <summary>Centro de custo a que a ação pertence; nulo é ação da casa, sem centro.</summary>
    public string? CostCenter { get; set; }

    public string Status { get; set; } = StatusDaAcao.Pendente;
    /// <summary>0 a 100. Ação concluída é 100, sempre — ver <c>PlanoDeAcaoService</c>.</summary>
    public int Progress { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
    public string? StatusReason { get; set; }

    public Guid CreatedBy { get; set; }
    public string CreatedByLabel { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}
