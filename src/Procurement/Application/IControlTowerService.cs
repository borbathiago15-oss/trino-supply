namespace TrinoSupply.Procurement.Application;

/// <summary>Um item da solicitação, com tudo o que aconteceu com ele até agora.</summary>
public sealed record ControlTowerRow(
    Guid RequisitionId, Guid LineId, DateTimeOffset CreatedAt, string Requester,
    string CostCenterCode, string CostCenterName, string ItemCode, decimal Quantity, string Unit,
    string Priority, DateOnly? NeededBy,
    string Stage, bool IsOpen, string Light,
    Guid? OrderId, long? OrderNumber, string? SupplierCode, string? SupplierName, string? Buyer,
    DateOnly? PromisedDate, decimal QuantityReceived, decimal PendingQuantity, decimal? OrderedValue,
    // Notas fiscais conciliadas contra a OC (números) e o pior veredito entre elas.
    string? InvoiceNumbers, string? InvoiceMatch);

/// <summary>Os cards do topo — todos derivados das mesmas linhas, sem consulta à parte.</summary>
public sealed record ControlTowerSummary(
    int OpenItems, int LateItems, int UrgentItems, int WithoutOrder,
    // Da criação da solicitação até a última entrega, nos itens concluídos na janela.
    double? AvgLeadTimeDays,
    // Valor das OCs emitidas que ainda não chegaram por inteiro.
    decimal BacklogValue);

public sealed record ControlTowerView(ControlTowerSummary Summary, IReadOnlyList<ControlTowerRow> Rows);

public sealed record ControlTowerFilter(
    int Days = 90, string? CostCenterCode = null, string? SupplierCode = null, string? Stage = null,
    bool OnlyLate = false, bool OnlyUrgent = false, bool OnlyWithoutOrder = false, string? Search = null);

/// <summary>
/// Torre de Controlo (Fase 04): visão unificada por ITEM, cruzando solicitação, cotação, OC,
/// recebimento e nota fiscal. Sem tabela própria: os indicadores derivam dos registros reais, então
/// nunca divergem do que aconteceu.
/// </summary>
public interface IControlTowerService
{
    Task<ControlTowerView> ListAsync(ControlTowerFilter filter, CancellationToken ct = default);
}
