using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Auth;

public record AuthTokens(string AccessToken, int ExpiresInSeconds, string RefreshToken, User User);

public class AuthService(AppDbContext db, TokenService tokens, IPasswordHasher<User> hasher, TimeProvider clock)
{
    /// <summary>Login por e-mail e senha. Retorna null em credencial inválida (sem distinguir usuário inexistente de senha errada).</summary>
    public async Task<AuthTokens?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalized && u.Active, ct);
        if (user is null) return null;

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            user.UpdatedAt = clock.GetUtcNow();
        }

        return await IssueTokensAsync(user, ct);
    }

    /// <summary>Rotaciona o refresh token. Reuso de token já rotacionado/revogado revoga todos os tokens do usuário.</summary>
    public async Task<AuthTokens?> RefreshAsync(string refreshTokenValue, CancellationToken ct = default)
    {
        var hash = TokenService.HashRefreshToken(refreshTokenValue);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null) return null;

        var now = clock.GetUtcNow();
        if (stored.RevokedAt is not null || now >= stored.ExpiresAt)
        {
            // reuso detectado: revoga a cadeia inteira do usuário (SEC-003)
            var active = await db.RefreshTokens
                .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ToListAsync(ct);
            foreach (var t in active) t.RevokedAt = now;
            await db.SaveChangesAsync(ct);
            return null;
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == stored.UserId && u.Active, ct);
        if (user is null) return null;

        var issued = await IssueTokensAsync(user, ct, saveChanges: false);
        stored.RevokedAt = now;
        stored.ReplacedById = db.ChangeTracker.Entries<RefreshToken>()
            .Single(e => e.State == EntityState.Added).Entity.Id;
        await db.SaveChangesAsync(ct);
        return issued;
    }

    public async Task LogoutAsync(string refreshTokenValue, CancellationToken ct = default)
    {
        var hash = TokenService.HashRefreshToken(refreshTokenValue);
        var stored = await db.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, ct);
        if (stored is null) return;
        stored.RevokedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    private async Task<AuthTokens> IssueTokensAsync(User user, CancellationToken ct, bool saveChanges = true)
    {
        var now = clock.GetUtcNow();
        var access = tokens.CreateAccessToken(user, now);
        var refreshValue = TokenService.GenerateRefreshTokenValue();

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = TokenService.HashRefreshToken(refreshValue),
            CreatedAt = now,
            ExpiresAt = now.AddDays(tokens.Options.RefreshTokenDays),
        });

        if (saveChanges) await db.SaveChangesAsync(ct);
        return new AuthTokens(access, tokens.Options.AccessTokenMinutes * 60, refreshValue, user);
    }
}
