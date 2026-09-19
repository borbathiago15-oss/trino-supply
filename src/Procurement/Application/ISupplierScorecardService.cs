using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Procurement.Application;

/// <summary>Desempenho de entrega de um fornecedor num período (ou na janela inteira).</summary>
public sealed record SupplierScoreView(
    Guid SupplierId, string SupplierCode, string SupplierName, string Period,
    int LinesEvaluated, int LinesWithDeadline, int LinesWithoutDeadline,
    decimal OnTimeRate, decimal InFullRate, decimal OtifIndex, decimal DamageRate,
    int Occurrences, string Tier);

/// <summary>Scorecard do fornecedor: consolidado da janela + a série por mês.</summary>
public sealed record SupplierScorecardView(
    Guid SupplierId, string SupplierCode, string SupplierName, int Months,
    SupplierScoreView Overall, IReadOnlyList<SupplierScoreView> Periods);

/// <summary>
/// OTIF do fornecedor (Fase 03) calculado a partir dos fatos reais: prazo prometido na OC × data da
/// conferência na doca, quantidade líquida × pedida, e as ocorrências registradas no recebimento.
/// Sem tabela consolidada: a fonte da verdade são as OCs e os recebimentos, então a nota nunca
/// diverge do que aconteceu (se o volume crescer, materializa-se numa projeção sem mudar o contrato).
/// </summary>
public interface ISupplierScorecardService
{
    Task<Result<SupplierScorecardView>> GetAsync(Guid supplierId, int months = 12, CancellationToken ct = default);

    /// <summary>Ranking de todos os fornecedores com entrega na janela — apoio à decisão de compra.</summary>
    Task<IReadOnlyList<SupplierScoreView>> ListAsync(int months = 12, CancellationToken ct = default);
}
