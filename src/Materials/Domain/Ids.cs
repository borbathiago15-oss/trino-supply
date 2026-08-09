namespace TrinoSupply.Materials.Domain;

/// <summary>Identidade de item (material) — MMS-002. Escopada ao tenant.</summary>
public readonly record struct ItemId(Guid Value)
{
    public static ItemId New() => new(Guid.NewGuid());
    public static ItemId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("ItemId não pode ser vazio.", nameof(value))
        : new ItemId(value);
    public override string ToString() => Value.ToString();
}

/// <summary>Identidade de unidade de medida — MMS-002, ADR-013. Escopada ao tenant.</summary>
public readonly record struct UnitId(Guid Value)
{
    public static UnitId New() => new(Guid.NewGuid());
    public static UnitId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("UnitId não pode ser vazio.", nameof(value))
        : new UnitId(value);
    public override string ToString() => Value.ToString();
}
