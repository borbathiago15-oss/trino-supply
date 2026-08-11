using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Procurement.Application;

public sealed record CostCenterView(
    Guid Id, string Code, string Name, string Status, string? PayingCompanyCode, string? PayingCompanyName);
public sealed record CostCenterInput(string Code, string Name, string? PayingCompanyCode = null);

/// <summary>Cadastro de centros de custo (spec Sistema de Compras). Base do cabeçalho da solicitação.</summary>
public interface ICostCenterService
{
    Task<Result<Guid>> CreateAsync(CostCenterInput input, CancellationToken ct = default);
    Task<IReadOnlyList<CostCenterView>> ListAsync(CancellationToken ct = default);
}
