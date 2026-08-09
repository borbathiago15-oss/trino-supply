using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Domain;
using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Foundation.Domain.Iam;

public enum UserStatus
{
    Active = 1,
    Inactive = 2
}

/// <summary>
/// Usuário da plataforma, escopado ao tenant (FD-001-01). Identidade externa (JWT) é ligada
/// pelo <see cref="Subject"/> (claim <c>sub</c>). Autorização é por papéis (deny-by-default).
/// </summary>
public sealed class User : AggregateRoot<UserId>, IBelongsToTenant
{
    // Backing em Guid puro para mapear direto a uuid[] no PostgreSQL; a API pública é tipada.
    private readonly List<Guid> _roleIds = new();

    private User(UserId id, CompanyId companyId, string subject, string email, string displayName) : base(id)
    {
        CompanyId = companyId;
        Subject = subject;
        Email = email;
        DisplayName = displayName;
        Status = UserStatus.Active;
    }

    // Exigido pelo EF Core.
    private User() : base(default!) { }

    public CompanyId CompanyId { get; private set; }

    /// <summary>Identificador do sujeito no provedor de identidade (claim <c>sub</c> do JWT).</summary>
    public string Subject { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public UserStatus Status { get; private set; }
    public IReadOnlyCollection<RoleId> RoleIds => _roleIds.Select(RoleId.From).ToArray();

    public static Result<User> Register(CompanyId companyId, string subject, string email, string displayName)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return Result.Failure<User>(new Error("iam.user.subject_required", "Subject (identidade externa) é obrigatório."));
        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure<User>(new Error("iam.user.email_required", "E-mail é obrigatório."));

        var user = new User(UserId.New(), companyId, subject.Trim(), email.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(displayName) ? email.Trim() : displayName.Trim());
        user.Raise(new UserRegistered(Guid.NewGuid(), DateTimeOffset.UtcNow, companyId.Value, user.Id.Value, user.Email));
        return Result.Success(user);
    }

    public Result AssignRole(RoleId roleId)
    {
        if (!_roleIds.Contains(roleId.Value))
            _roleIds.Add(roleId.Value);
        Version++;
        return Result.Success();
    }

    public Result RemoveRole(RoleId roleId)
    {
        _roleIds.Remove(roleId.Value);
        Version++;
        return Result.Success();
    }

    public void Deactivate()
    {
        Status = UserStatus.Inactive;
        Version++;
    }
}

/// <summary>Evento de negócio: usuário registrado (ADR-010).</summary>
public sealed record UserRegistered(Guid EventId, DateTimeOffset OccurredAt, Guid CompanyId, Guid UserId, string Email)
    : IDomainEvent;
