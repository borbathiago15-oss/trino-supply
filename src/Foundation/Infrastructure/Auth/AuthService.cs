using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.BuildingBlocks;
using TrinoSupply.BuildingBlocks.Abstractions;
using TrinoSupply.BuildingBlocks.Security;
using TrinoSupply.Foundation.Application.Auth;
using TrinoSupply.Foundation.Domain.Iam;
using TrinoSupply.Foundation.Infrastructure.Persistence;

namespace TrinoSupply.Foundation.Infrastructure.Auth;

/// <summary>Autenticação por e-mail/senha e ciclo de refresh tokens (SEC-001). Ver <see cref="IAuthService"/>.</summary>
public sealed class AuthService(
    FoundationDbContext db, IPasswordHasher hasher, ITokenIssuer issuer, IClock clock) : IAuthService
{
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);
    private static readonly Error InvalidCredentials = new("auth.invalid_credentials", "Credenciais inválidas.");
    private static readonly Error InvalidRefresh = new("auth.invalid_refresh", "Refresh token inválido ou expirado.");

    public async Task<Result<AuthTokens>> LoginAsync(Guid companyId, string email, string password, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await SetTenantAsync(companyId, ct);

        var normalized = email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null || user.Status != UserStatus.Active || user.PasswordHash is null
            || !hasher.Verify(user.PasswordHash, password))
            return Result.Failure<AuthTokens>(InvalidCredentials);

        var tokens = await IssueAsync(user, ct);
        await tx.CommitAsync(ct);
        return Result.Success(tokens);
    }

    public async Task<Result<AuthTokens>> RefreshAsync(Guid companyId, string refreshToken, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await SetTenantAsync(companyId, ct);

        var hash = HashToken(refreshToken);
        var rt = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (rt is null || !rt.IsActive(clock.UtcNow))
            return Result.Failure<AuthTokens>(InvalidRefresh);

        rt.Revoke(clock.UtcNow); // rotação: o token usado é invalidado

        var user = await db.Users.FindAsync([UserId.From(rt.UserId)], ct);
        if (user is null || user.Status != UserStatus.Active)
            return Result.Failure<AuthTokens>(InvalidRefresh);

        var tokens = await IssueAsync(user, ct);
        await tx.CommitAsync(ct);
        return Result.Success(tokens);
    }

    public async Task<Result> LogoutAsync(Guid companyId, string refreshToken, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await SetTenantAsync(companyId, ct);

        var hash = HashToken(refreshToken);
        var rt = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (rt is not null)
        {
            rt.Revoke(clock.UtcNow);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return Result.Success(); // idempotente
    }

    private async Task<AuthTokens> IssueAsync(User user, CancellationToken ct)
    {
        var access = issuer.Issue(user.CompanyId.Value, user.Subject);
        var raw = Base64Url(RandomNumberGenerator.GetBytes(32));
        var rt = RefreshToken.Issue(user.CompanyId, user.Id.Value, HashToken(raw), clock.UtcNow, RefreshLifetime);
        db.RefreshTokens.Add(rt);
        await db.SaveChangesAsync(ct);
        return new AuthTokens(access.Token, access.ExpiresAt, raw, rt.ExpiresAt);
    }

    private Task SetTenantAsync(Guid companyId, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT set_config('app.current_company', {0}, true)", companyId.ToString());

    private static string HashToken(string raw) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
