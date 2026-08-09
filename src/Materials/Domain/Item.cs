using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Materials.Domain;

public enum ItemStatus
{
    Active = 1,
    Inactive = 2
}

/// <summary>
/// Item (material) — MMS-002. Identificado por um código (SKU) único no tenant, com uma unidade de
/// medida base (ADR-013). O saldo de estoque é uma projeção separada (fatia posterior).
/// </summary>
public sealed class Item : AggregateRoot<ItemId>, IBelongsToTenant
{
    private Item(ItemId id, CompanyId companyId, string code, string name, UnitId baseUnitId) : base(id)
    {
        CompanyId = companyId;
        Code = code;
        Name = name;
        BaseUnitId = baseUnitId;
        Status = ItemStatus.Active;
    }

    // Exigido pelo EF Core.
    private Item() : base(default!) { }

    public CompanyId CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public UnitId BaseUnitId { get; private set; }
    public ItemStatus Status { get; private set; }

    public static Result<Item> Create(CompanyId companyId, string code, string name, UnitId baseUnitId)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<Item>(new Error("materials.item.code_required", "Código do item é obrigatório."));
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Item>(new Error("materials.item.name_required", "Nome do item é obrigatório."));

        return Result.Success(new Item(ItemId.New(), companyId, code.Trim().ToUpperInvariant(), name.Trim(), baseUnitId));
    }

    public void Deactivate()
    {
        Status = ItemStatus.Inactive;
        Version++;
    }
}
