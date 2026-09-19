using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Procurement.Application;

public sealed record CompliancePenaltyView(string Rule, int Points, string Title, string Evidence);

/// <summary>
/// O Compliance Score de um processo de compra, com as penalidades que explicam o número e os fatos
/// que as sustentam — o auditor não precisa refazer a conta para entender.
/// </summary>
public sealed record ComplianceView(
    Guid OrderId, long OrderNumber, Guid RequisitionId, string SupplierCode, string SupplierName,
    DateTimeOffset IssuedAt, decimal NetValue, int Score, string Band, string Summary,
    bool Emergencial, DateOnly? NeededBy, DateOnly CreatedOn, bool SupplierHomologated,
    int QuotationResponses, bool AwardedOutsideLowest, bool AwardJustified,
    IReadOnlyList<CompliancePenaltyView> Penalties);

/// <summary>
/// Compliance Score (Fase 05): auditoria contínua dos processos de compra. Calculado a partir dos
/// registros do próprio sistema — solicitação, cotação e cadastro do fornecedor — e <b>sem tabela
/// consolidada</b>, pelo mesmo motivo do OTIF: a nota nunca diverge do que aconteceu.
/// <para>
/// Uma ressalva honesta: a homologação do fornecedor é lida <b>como está hoje</b>. Se alguém for
/// desomologado depois, compras antigas feitas com ele passam a pontuar pior — o que é o desejado
/// para varredura de risco, mas não é a foto do dia da compra.
/// </para>
/// </summary>
public interface IComplianceService
{
    Task<Result<ComplianceView>> GetAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>Varredura da auditoria: os processos ordenados do pior score para o melhor.</summary>
    Task<IReadOnlyList<ComplianceView>> ListAsync(int? maxScore = null, int limit = 200, CancellationToken ct = default);
}
