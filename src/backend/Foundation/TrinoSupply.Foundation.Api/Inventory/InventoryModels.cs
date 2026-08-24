namespace TrinoSupply.Foundation.Api.Inventory;

/// <summary>Local de armazenagem (MMS-004 MVP: nível depósito; endereçamento é evolução).</summary>
public class StorageLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}

/// <summary>
/// Projeção de saldo por item × local (MMS-004, MMS-P-08): NUNCA editada diretamente —
/// todo efeito vem da confirmação de um StockMovement (InventoryService é o único escritor).
/// </summary>
public class StockBalance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CatalogItemId { get; set; }
    public Guid LocationId { get; set; }
    public decimal TotalQty { get; set; }
    public decimal ReservedQty { get; set; }
    public decimal AvailableQty => TotalQty - ReservedQty;
    public Guid LastMovementId { get; set; }
    public DateTimeOffset LastMovementAt { get; set; }
    public int Version { get; set; } = 1;
}

public enum MovementType : short
{
    Entry = 1,       // entrada (recebimento MMS-005, devolução, carga inicial)
    Issue = 2,       // saída (atendimento MMS-003, consumo)
}

public enum MovementOrigin : short
{
    Receiving = 1,     // MMS-005 — recebimento conferido (nota/documento externo)
    Return = 2,        // devolução de solicitante
    InitialLoad = 3,   // carga inicial documentada
    Fulfillment = 4,   // atendimento de solicitação de material (MMS-003)
    Consumption = 5,   // consumo interno autorizado
}

/// <summary>
/// Documento de movimentação (MMS-P-07: nenhuma movimentação sem documento).
/// Imutável após confirmado — o MVP confirma na criação; correção é por movimento inverso.
/// </summary>
public class StockMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = string.Empty;          // MOV-2026-000001
    public MovementType Type { get; set; }
    public MovementOrigin Origin { get; set; }
    public string OriginReference { get; set; } = string.Empty; // nota fiscal, nº da solicitação etc.
    public Guid CatalogItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;        // snapshot
    public string ItemDescription { get; set; } = string.Empty; // snapshot
    public string UnitOfMeasure { get; set; } = "UN";
    public Guid LocationId { get; set; }
    public decimal Quantity { get; set; }
    public decimal BalanceBefore { get; set; }                  // saldo total antes/depois (IV — auditoria)
    public decimal BalanceAfter { get; set; }
    public Guid? MaterialRequisitionId { get; set; }            // vínculo MMS-003 quando atendimento
    public Guid PerformedBy { get; set; }
    public string PerformedByLabel { get; set; } = string.Empty;
    public DateTimeOffset PerformedAt { get; set; }
}
