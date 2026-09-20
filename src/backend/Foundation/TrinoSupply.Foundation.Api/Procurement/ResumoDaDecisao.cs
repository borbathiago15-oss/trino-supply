using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Compliance;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Procurement;

/// <summary>
/// O que quem aprova precisa ver <b>sem abrir o processo</b>: quem pediu e por quê, a
/// urgência, o orçamento, há quanto tempo a decisão espera, o compliance e o contrato.
/// O preço e a comparação entre propostas já vão na vista do processo; isto é o que a
/// vista não tinha, porque vem da SC de origem e do compliance.
/// </summary>
public record DecisaoResumo(
    string RequesterLabel, string Priority, string? UrgencyReason, string? UrgencyImpact,
    DateOnly? NeededBy, decimal? Budget, int Level, DateTimeOffset? WaitingSince,
    int? ComplianceScore, IReadOnlyList<CompliancePenalty> CompliancePenalties, string? ContractNumber);

/// <summary>Os fatos da SC de origem que a decisão usa. Várias SCs no mesmo processo se consolidam.</summary>
public record FatosDaSc(string RequesterLabel, string Priority, string? UrgencyReason, string? UrgencyImpact,
    DateOnly? NeededBy, decimal? Budget);

public static class ResumoDaDecisao
{
    /// <summary>
    /// Consolida as SCs de origem: solicitantes sem repetição, urgente se qualquer uma for,
    /// a data de necessidade mais próxima, e o orçamento só quando <b>todas</b> informaram o
    /// seu — a mesma regra do saving de orçamento, porque somar um orçamento parcial diria
    /// ao aprovador que a compra estourou uma meta que ninguém deu.
    /// </summary>
    public static DecisaoResumo Consolidar(Quotation q, IReadOnlyList<FatosDaSc> scs,
        int? complianceScore = null, IReadOnlyList<CompliancePenalty>? penalties = null, string? contractNumber = null)
    {
        var solicitantes = string.Join(", ", scs.Select(s => s.RequesterLabel).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
        var urgente = scs.FirstOrDefault(s => s.Priority == "URGENT");
        var prioridade = urgente is not null ? "URGENT" : scs.FirstOrDefault()?.Priority ?? "NORMAL";
        var necessidade = scs.Where(s => s.NeededBy is not null).Select(s => s.NeededBy!.Value).DefaultIfEmpty().Min();
        var orcamento = scs.Count > 0 && scs.All(s => s.Budget is not null) ? scs.Sum(s => s.Budget!.Value) : (decimal?)null;
        var nivel = q.Status == QuotationStatus.AwaitingDirector ? 2 : 1;
        // cada alçada conta pelo próprio relógio: o Nível 2 espera desde o Nível 1, não desde a escolha
        var desde = nivel == 2 ? q.ManagerApprovedAt : q.SelectedAt;
        return new DecisaoResumo(solicitantes, prioridade, urgente?.UrgencyReason, urgente?.UrgencyImpact,
            necessidade == default ? null : necessidade, orcamento, nivel, desde,
            complianceScore, penalties ?? [], contractNumber);
    }

    /// <summary>Monta o resumo de cada processo da fila, com o mínimo de idas ao banco.</summary>
    public static async Task<Dictionary<Guid, DecisaoResumo>> MontarAsync(
        AppDbContext db, ComplianceService compliance, TimeProvider clock, IReadOnlyList<Quotation> fila, CancellationToken ct)
    {
        var saida = new Dictionary<Guid, DecisaoResumo>();
        if (fila.Count == 0) return saida;

        var prIds = fila.SelectMany(q => q.SourcePrIds).Distinct().ToList();
        var prs = await db.Requisitions.Where(r => prIds.Contains(r.Id))
            .Select(r => new { r.Id, r.RequesterLabel, r.Priority, r.UrgencyReason, r.UrgencyImpact, r.NeededBy, r.Budget })
            .ToDictionaryAsync(r => r.Id, ct);

        var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var vencedores = fila.Where(q => q.WinnerSupplierId is not null).Select(q => q.WinnerSupplierId!.Value).Distinct().ToList();
        var contratos = await db.Suppliers.Include(s => s.ContractItems)
            .Where(s => vencedores.Contains(s.Id) && s.ContractNumber != null)
            .ToDictionaryAsync(s => s.Id, ct);

        foreach (var q in fila)
        {
            var scs = q.SourcePrIds.Where(prs.ContainsKey).Select(id => prs[id])
                .Select(r => new FatosDaSc(r.RequesterLabel, r.Priority, r.UrgencyReason, r.UrgencyImpact, r.NeededBy, r.Budget))
                .ToList();
            // o compliance mede e expõe, nunca bloqueia: aqui ele só entra no card
            var linha = (await compliance.ReportAsync(q.Id, ct)).Items.FirstOrDefault();
            var contrato = q.WinnerSupplierId is { } sid && contratos.TryGetValue(sid, out var s) && s.ContractIsCurrent(hoje)
                ? s.ContractNumber : null;
            saida[q.Id] = Consolidar(q, scs, linha?.Score, linha?.Penalties, contrato);
        }
        return saida;
    }
}

/// <summary>Uma decisão que quem está logado tomou: a memória curta de "o que eu já resolvi".</summary>
public record DecisaoRecente(Guid QuotationId, string Number, QuotationStatus Status, string EventType,
    DateTimeOffset OccurredAt, string? Note, string? SupplierName, decimal? TotalValue);
