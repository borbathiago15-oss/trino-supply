using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Materials.Application;

public sealed record ConsumptionLineInput(string ItemCode, decimal Quantity);
public sealed record ConsumptionLineView(string ItemCode, decimal Quantity);

/// <summary>Baixa de consumo: empresa + centro + colaborador + motivo + produtos entregues.</summary>
public sealed record CreateConsumptionInput(
    string CompanyCode, string CostCenterCode, Guid CollaboratorId, string Reason,
    IReadOnlyList<ConsumptionLineInput> Lines);

public sealed record ConsumptionView(
    Guid Id, string CompanyCode, string CostCenterCode, Guid CollaboratorId, string CollaboratorName,
    string Reason, string IssuedBy, DateTimeOffset IssuedAt, IReadOnlyList<ConsumptionLineView> Lines);

/// <summary>Linha da ficha de entrega: descrição, código/CA (CA quando EPI), quantidade e data de entrega.</summary>
public sealed record FichaLine(string Description, string ItemCode, string Group, string? Ca, decimal Quantity, DateTimeOffset DeliveredAt);

/// <summary>Dados da Ficha de Entrega de EPI/Uniformes pré-preenchida (spec Almoxarifado) — para assinatura.</summary>
public sealed record ConsumptionFicha(
    string CompanyCode, string CostCenterCode, string CollaboratorName, string? Registration,
    DateOnly? AdmissionDate, string Reason, string IssuedBy, DateTimeOffset IssuedAt, IReadOnlyList<FichaLine> Lines);

/// <summary>
/// Baixa de consumo (spec Almoxarifado — entrega ao colaborador). Cada linha gera uma saída no ledger
/// (com garantia de saldo); a baixa inteira é atômica (falta de saldo em qualquer item aborta tudo).
/// </summary>
public interface IConsumptionService
{
    Task<Result<Guid>> CreateAsync(CreateConsumptionInput input, CancellationToken ct = default);
    Task<IReadOnlyList<ConsumptionView>> ListAsync(CancellationToken ct = default);
    /// <summary>Monta os dados da ficha de entrega (para gerar o PDF pré-preenchido).</summary>
    Task<Result<ConsumptionFicha>> GetFichaAsync(Guid id, CancellationToken ct = default);
}
