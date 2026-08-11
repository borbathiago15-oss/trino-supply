using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Materials.Domain;

/// <summary>Sentido do movimento de estoque (MMS-002).</summary>
public enum StockDirection
{
    In = 1,   // entrada
    Out = 2   // saída
}

/// <summary>
/// Saldo de estoque de um item — PROJEÇÃO (ADR-009): reflete a soma do ledger de movimentos.
/// Identidade = o próprio item (um saldo por item por tenant). A concorrência é resolvida por
/// serialização na chave de saldo (lock <c>FOR UPDATE</c>) + <see cref="Entity{TId}.Version"/> como
/// guarda otimista — entradas/saídas simultâneas não se perdem (ARC-006 §12).
/// </summary>
public sealed class StockBalance : AggregateRoot<ItemId>, IBelongsToTenant
{
    private StockBalance(ItemId itemId, CompanyId companyId) : base(itemId)
    {
        CompanyId = companyId;
        Quantity = 0m;
    }

    // Exigido pelo EF Core.
    private StockBalance() : base(default!) { }

    public CompanyId CompanyId { get; private set; }

    /// <summary>Quantidade em mãos, na unidade-base do item.</summary>
    public decimal Quantity { get; private set; }

    public static StockBalance Create(CompanyId companyId, ItemId itemId) => new(itemId, companyId);

    /// <summary>Aplica uma variação (positiva=entrada, negativa=saída). Não permite saldo negativo.</summary>
    public Result Apply(decimal delta)
    {
        var next = Quantity + delta;
        if (next < 0)
            return Result.Failure(new Error("materials.stock.insufficient", "Saldo insuficiente para a saída."));

        Quantity = next;
        Version++;
        return Result.Success();
    }
}

/// <summary>
/// Movimento de estoque — fato imutável no ledger (append-only). O saldo é derivado da soma dos
/// movimentos; este registro é a fonte de verdade e a trilha de rastreabilidade (MMS-002).
/// </summary>
public sealed class StockMovement : IBelongsToTenant
{
    private StockMovement(
        Guid id, CompanyId companyId, ItemId itemId, StockDirection direction,
        decimal quantity, DateTimeOffset occurredAt, string? reason, string? costCenterCode)
    {
        Id = id;
        CompanyId = companyId;
        ItemId = itemId;
        Direction = direction;
        Quantity = quantity;
        OccurredAt = occurredAt;
        Reason = reason;
        CostCenterCode = costCenterCode;
    }

    // Exigido pelo EF Core.
    private StockMovement() { }

    public Guid Id { get; private set; }
    public CompanyId CompanyId { get; private set; }
    public ItemId ItemId { get; private set; }
    public StockDirection Direction { get; private set; }

    /// <summary>Quantidade sempre positiva; o <see cref="Direction"/> dá o sentido.</summary>
    public decimal Quantity { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? Reason { get; private set; }

    /// <summary>Centro de custo debitado (spec v2: TODA saída informa o centro; entrada é opcional).</summary>
    public string? CostCenterCode { get; private set; }

    public static StockMovement Create(
        CompanyId companyId, ItemId itemId, StockDirection direction,
        decimal quantity, DateTimeOffset occurredAt, string? reason, string? costCenterCode = null) =>
        new(Guid.NewGuid(), companyId, itemId, direction, quantity, occurredAt, reason,
            string.IsNullOrWhiteSpace(costCenterCode) ? null : costCenterCode.Trim().ToUpperInvariant());
}
