namespace TrinoSupply.Procurement.Domain;

/// <summary>Desvio de governança que deduz pontos do processo de compra (Fase 05).</summary>
public enum ComplianceDeduction
{
    SemConcorrencia = 1,                    // proposta única ou compra direta
    FornecedorNaoHomologado = 2,            // comprou de quem não está homologado
    CompraEmergencial = 3,                  // urgência atropela o planejamento
    NecessidadeRetroativa = 4,              // precisava antes de a solicitação existir
    ForaDoMenorPrecoSemJustificativa = 5,   // escolheu o mais caro sem dizer por quê
}

/// <summary>Uma dedução aplicada, com a evidência que a sustenta na auditoria.</summary>
public sealed record CompliancePenalty(ComplianceDeduction Rule, int Points, string Title, string Evidence);

/// <summary>
/// Os fatos do processo de compra que a auditoria observa. Nenhum deles é opinião: todos saem de
/// registros do próprio sistema (solicitação, cotação, cadastro do fornecedor).
/// </summary>
public sealed record ComplianceFacts(
    bool Emergencial,
    DateOnly? NeededBy,
    DateOnly CreatedOn,
    bool SupplierHomologated,
    // Quantos fornecedores efetivamente responderam. Negativo = compra sem cotação.
    int QuotationResponses,
    bool AwardedOutsideLowest,
    bool AwardJustified);

public sealed record ComplianceResult(int Score, IReadOnlyList<CompliancePenalty> Penalties)
{
    /// <summary>Faixa para leitura rápida — o número sozinho não diz se é caso de olhar.</summary>
    public string Band => Score switch
    {
        >= 90 => "Exemplar",
        >= 70 => "Aceitavel",
        >= 50 => "Atencao",
        _ => "Critico",
    };

    /// <summary>Uma linha resumindo o que pesou — é o que cabe num badge.</summary>
    public string Summary => Penalties.Count == 0
        ? "Sem desvios"
        : string.Join(" · ", Penalties.Select(p => $"−{p.Points} {p.Title}"));
}

/// <summary>
/// Compliance Score do processo de compra (Fase 05): parte de 100 e desconta os desvios de
/// governança observados, cada um com a evidência que o sustenta. Serve à auditoria contínua — o
/// índice <b>não bloqueia nada</b>, aponta onde olhar.
/// <para>
/// Cálculo puro e determinístico: os mesmos fatos dão sempre a mesma nota, e a lista de penalidades
/// explica o número sem exigir que alguém refaça a conta à mão.
/// </para>
/// </summary>
public static class ComplianceScore
{
    private const int MaxScore = 100;

    public static ComplianceResult Evaluate(ComplianceFacts f)
    {
        var penalidades = new List<CompliancePenalty>();

        // Sem disputa não há preço de mercado: ou não houve cotação, ou só um fornecedor respondeu.
        if (f.QuotationResponses < 2)
        {
            penalidades.Add(new CompliancePenalty(
                ComplianceDeduction.SemConcorrencia, 25, "sem concorrência",
                f.QuotationResponses < 0
                    ? "OC emitida sem passar por cotação."
                    : f.QuotationResponses == 0
                        ? "Cotação aberta, mas nenhum fornecedor respondeu."
                        : "Apenas uma proposta recebida — não houve disputa de preço."));
        }

        if (!f.SupplierHomologated)
        {
            penalidades.Add(new CompliancePenalty(
                ComplianceDeduction.FornecedorNaoHomologado, 30, "fornecedor não homologado",
                "O fornecedor da OC não está com cadastro ativo/homologado."));
        }

        if (f.Emergencial)
        {
            penalidades.Add(new CompliancePenalty(
                ComplianceDeduction.CompraEmergencial, 20, "compra emergencial",
                "Solicitação aberta como Emergencial, fora do fluxo de planejamento."));
        }

        // Precisar do material antes de pedir é o sintoma clássico da compra já feita sendo
        // formalizada depois.
        if (f.NeededBy is { } prazo && prazo < f.CreatedOn)
        {
            penalidades.Add(new CompliancePenalty(
                ComplianceDeduction.NecessidadeRetroativa, 20, "necessidade retroativa",
                $"Data de necessidade ({prazo:dd/MM/yyyy}) anterior à criação da solicitação ({f.CreatedOn:dd/MM/yyyy})."));
        }

        if (f.AwardedOutsideLowest && !f.AwardJustified)
        {
            penalidades.Add(new CompliancePenalty(
                ComplianceDeduction.ForaDoMenorPrecoSemJustificativa, 15, "escolha sem justificativa",
                "Vencedor diferente do menor preço, sem justificativa registrada na adjudicação."));
        }

        var score = Math.Clamp(MaxScore - penalidades.Sum(p => p.Points), 0, MaxScore);
        return new ComplianceResult(score, penalidades);
    }
}
