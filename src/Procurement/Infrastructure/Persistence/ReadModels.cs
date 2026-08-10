namespace TrinoSupply.Procurement.Infrastructure.Persistence;

/// <summary>
/// Projeção (read model) do histórico de compras por fornecedor — alimentada de forma <b>assíncrona</b>
/// pelo consumidor de eventos (OrderIssued/OrderCancelled). Escopada ao tenant (RLS).
/// </summary>
public sealed class SupplierStats
{
    public Guid CompanyId { get; set; }
    public Guid SupplierId { get; set; }
    public int OrdersCount { get; set; }
    public decimal TotalValue { get; set; }
    public DateTimeOffset? LastOrderAt { get; set; }
}

/// <summary>
/// Registro de idempotência do consumidor: garante que um evento (EventId) seja aplicado à projeção
/// no máximo uma vez, mesmo com entrega <i>at-least-once</i> do broker. Tabela de infra (sem RLS).
/// </summary>
public sealed class ProcessedEvent
{
    public Guid EventId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
