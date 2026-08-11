namespace TrinoSupply.Materials.Application;

/// <summary>Consumo (saídas) agregado por centro de custo e item, na janela pedida.</summary>
public sealed record CenterConsumptionView(
    string CostCenterCode, string ItemCode, string ItemName, decimal TotalQuantity, int Movements);

/// <summary>Entregas de EPI/fardamento agregadas por colaborador, na janela pedida.</summary>
public sealed record CollaboratorConsumptionView(
    string CollaboratorName, string? Registration, string CostCenterCode, int Deliveries, decimal TotalItems);

/// <summary>
/// Analytics do Almox (v3): agora que TODA saída carrega o centro de custo, o consumo é
/// rastreável por centro e por colaborador — leituras agregadas para os dashboards.
/// </summary>
public interface IMaterialsAnalytics
{
    Task<IReadOnlyList<CenterConsumptionView>> ConsumptionByCenterAsync(int days = 90, CancellationToken ct = default);
    Task<IReadOnlyList<CollaboratorConsumptionView>> ConsumptionByCollaboratorAsync(int days = 90, CancellationToken ct = default);
}
