using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Foundation.Domain.Iam;

/// <summary>
/// Token de definição de senha (convite de novo usuário ou "esqueci minha senha" — SEC-001).
/// Guardamos apenas o HASH (nunca o valor em claro); uso único e com expiração curta.
/// Escopado ao tenant (RLS).
/// </summary>
public sealed class PasswordSetupToken : IBelongsToTenant
{
    private PasswordSetupToken(
        Guid id, CompanyId companyId, Guid userId, string tokenHash,
        DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        Id = id;
        CompanyId = companyId;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    // Exigido pelo EF Core.
    private PasswordSetupToken() { }

    public Guid Id { get; private set; }
    public CompanyId CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }

    public static PasswordSetupToken Issue(
        CompanyId companyId, Guid userId, string tokenHash, DateTimeOffset now, TimeSpan lifetime) =>
        new(Guid.NewGuid(), companyId, userId, tokenHash, now, now.Add(lifetime));

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && now < ExpiresAt;

    public void MarkUsed(DateTimeOffset now) => UsedAt ??= now;
}
