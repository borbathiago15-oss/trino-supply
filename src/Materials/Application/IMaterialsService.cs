using TrinoSupply.BuildingBlocks;

namespace TrinoSupply.Materials.Application;

public sealed record UnitView(Guid Id, string Code, string Name, string Dimension, decimal FactorToBase);
public sealed record ItemView(Guid Id, string Code, string Name, Guid BaseUnitId, string Status, string Group);

/// <summary>Linha da importação em lote de itens (planilha).</summary>
public sealed record ItemImportRow(string Code, string Name, string BaseUnitCode, string? Group);
public sealed record ItemImportResult(int Imported, IReadOnlyList<string> Errors);

/// <summary>Casos de uso de Materiais (MMS-002): catálogo de itens e unidades + conversão (ADR-013).</summary>
public interface IMaterialsService
{
    Task<Result<Guid>> CreateUnitAsync(string code, string name, string dimension, decimal factorToBase, CancellationToken ct = default);
    Task<IReadOnlyList<UnitView>> ListUnitsAsync(CancellationToken ct = default);

    /// <summary>Cria um item com a unidade-base referenciada por código e um grupo/família.</summary>
    Task<Result<Guid>> CreateItemAsync(string code, string name, string baseUnitCode, string? group = null, CancellationToken ct = default);

    /// <summary>Lista itens, opcionalmente filtrando por grupo/família.</summary>
    Task<IReadOnlyList<ItemView>> ListItemsAsync(string? group = null, CancellationToken ct = default);

    /// <summary>Importa itens em lote (planilha); cria unidades ausentes automaticamente. Não lança.</summary>
    Task<ItemImportResult> ImportItemsAsync(IReadOnlyList<ItemImportRow> rows, CancellationToken ct = default);

    /// <summary>Converte uma quantidade entre unidades (mesma dimensão) — ADR-013.</summary>
    Task<Result<decimal>> ConvertAsync(decimal quantity, string fromUnitCode, string toUnitCode, CancellationToken ct = default);
}
