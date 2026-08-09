using TrinoSupply.BuildingBlocks.Multitenancy;

namespace TrinoSupply.Foundation.Domain.Iam;

/// <summary>
/// Refresh token persistido (SEC-001). Guardamos apenas o HASH do token (nunca o valor em claro),
/// com expiração e revogação. Rotação: a cada refresh o token usado é revogado e um novo é emitido.
/// Escopado ao tenant.
/// </summary>
public sealed class RefreshToken : IBelongsToTenant
{
    private RefreshToken(
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
    private RefreshToken() { }

    public Guid Id { get; private set; }
    public CompanyId CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static RefreshToken Issue(
        CompanyId companyId, Guid userId, string tokenHash, DateTimeOffset now, TimeSpan lifetime) =>
        new(Guid.NewGuid(), companyId, userId, tokenHash, now, now.Add(lifetime));

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
