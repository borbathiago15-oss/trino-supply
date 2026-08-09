namespace TrinoSupply.Foundation.Domain.Iam;

/// <summary>Identidade de usuário (FD-001-01). Escopada ao tenant (CompanyId).</summary>
public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.NewGuid());

    public static UserId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("UserId não pode ser vazio.", nameof(value))
        : new UserId(value);

    public override string ToString() => Value.ToString();
}

/// <summary>Identidade de papel (FD-001-01). Escopada ao tenant (CompanyId).</summary>
public readonly record struct RoleId(Guid Value)
{
    public static RoleId New() => new(Guid.NewGuid());

    public static RoleId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("RoleId não pode ser vazio.", nameof(value))
        : new RoleId(value);

    public override string ToString() => Value.ToString();
}
