namespace TrinoSupply.Foundation.Api.Domain;

/// <summary>
/// Refresh token rotativo (FD-001-01 / SEC-003): armazenado apenas como hash SHA-256.
/// Cada uso rotaciona o token; reuso de token já rotacionado revoga a cadeia inteira.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedById { get; set; }

    public bool IsActive => RevokedAt is null && DateTimeOffset.UtcNow < ExpiresAt;
}
