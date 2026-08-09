using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Foundation.Domain.Iam;

/// <summary>
/// Papel: conjunto nomeado de permissões, escopado ao tenant (FD-001-01). Um usuário recebe
/// papéis; suas permissões efetivas são a união das permissões dos papéis (deny-by-default).
/// </summary>
public sealed class Role : AggregateRoot<RoleId>, IBelongsToTenant
{
    // List (não HashSet): EF mapeia coleções primitivas apenas como array/IList → text[].
    private readonly List<string> _permissions = new();

    private Role(RoleId id, CompanyId companyId, string name) : base(id)
    {
        CompanyId = companyId;
        Name = name;
    }

    // Exigido pelo EF Core.
    private Role() : base(default!) { }

    public CompanyId CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public IReadOnlyCollection<string> Permissions => _permissions;

    public static Result<Role> Create(CompanyId companyId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Role>(new Error("iam.role.name_required", "Nome do papel é obrigatório."));

        var role = new Role(RoleId.New(), companyId, name.Trim());
        role.Raise(new RoleCreated(Guid.NewGuid(), DateTimeOffset.UtcNow, companyId.Value, role.Id.Value, role.Name));
        return Result.Success(role);
    }

    public Result Grant(string permission)
    {
        if (!PermissionCatalog.IsKnown(permission))
            return Result.Failure(new Error("iam.permission.unknown", $"Permissão desconhecida: {permission}."));

        if (!_permissions.Contains(permission))
            _permissions.Add(permission);
        Version++;
        return Result.Success();
    }

    public Result Revoke(string permission)
    {
        _permissions.Remove(permission);
        Version++;
        return Result.Success();
    }

    public bool Has(string permission) => _permissions.Contains(permission);
}

/// <summary>Evento de negócio: papel criado (ADR-010).</summary>
public sealed record RoleCreated(Guid EventId, DateTimeOffset OccurredAt, Guid CompanyId, Guid RoleId, string Name)
    : IDomainEvent;
