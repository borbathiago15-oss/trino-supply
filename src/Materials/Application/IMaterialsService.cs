using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Materials.Application;

public sealed record UnitView(Guid Id, string Code, string Name, string Dimension, decimal FactorToBase);
public sealed record ItemView(Guid Id, string Code, string Name, Guid BaseUnitId, string Status);

/// <summary>Casos de uso de Materiais (MMS-002): catálogo de itens e unidades + conversão (ADR-013).</summary>
public interface IMaterialsService
{
    Task<Result<Guid>> CreateUnitAsync(string code, string name, string dimension, decimal factorToBase, CancellationToken ct = default);
    Task<IReadOnlyList<UnitView>> ListUnitsAsync(CancellationToken ct = default);

    /// <summary>Cria um item com a unidade-base referenciada por código.</summary>
    Task<Result<Guid>> CreateItemAsync(string code, string name, string baseUnitCode, CancellationToken ct = default);
    Task<IReadOnlyList<ItemView>> ListItemsAsync(CancellationToken ct = default);

    /// <summary>Converte uma quantidade entre unidades (mesma dimensão) — ADR-013.</summary>
    Task<Result<decimal>> ConvertAsync(decimal quantity, string fromUnitCode, string toUnitCode, CancellationToken ct = default);
}
