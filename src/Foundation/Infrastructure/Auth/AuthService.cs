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
    FoundationDbContext db, IPasswordHasher hasher, ITokenIssuer issuer, IClock clock, ILoginThrottle throttle) : IAuthService
{
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);
    private static readonly Error InvalidCredentials = new("auth.invalid_credentials", "Credenciais inválidas.");
    private static readonly Error InvalidRefresh = new("auth.invalid_refresh", "Refresh token inválido ou expirado.");
    private static readonly Error Locked = new(
        "auth.locked", "Conta temporariamente bloqueada por tentativas seguidas. Tente novamente em alguns minutos.");

    public async Task<Result<AuthTokens>> LoginAsync(Guid companyId, string email, string password, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var throttleKey = $"{companyId:N}:{normalized}";
        if (throttle.IsLocked(throttleKey))
            return Result.Failure<AuthTokens>(Locked);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await SetTenantAsync(companyId, ct);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null || user.Status != UserStatus.Active || user.PasswordHash is null
            || !hasher.Verify(user.PasswordHash, password))
        {
            throttle.RegisterFailure(throttleKey);
            // Auditoria da falha somente quando a conta existe (evita poluir a trilha com lixo anônimo);
            // o freio de força bruta acima vale de qualquer forma.
            if (user is not null)
            {
                db.AuditEntries.Add(Domain.Audit.AuditEntry.Create(
                    user.CompanyId, clock.UtcNow, normalized, "auth.login_failed", "User", user.Id.Value.ToString(), null));
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            return Result.Failure<AuthTokens>(InvalidCredentials);
        }

        throttle.Reset(throttleKey);

        // Higiene: expurga tokens do usuário já revogados/expirados há mais de 7 dias (tabela não cresce sem limite).
        var cutoff = clock.UtcNow.AddDays(-7);
        await db.RefreshTokens
            .Where(t => t.UserId == user.Id.Value && (t.RevokedAt != null || t.ExpiresAt < cutoff))
            .ExecuteDeleteAsync(ct);

        db.AuditEntries.Add(Domain.Audit.AuditEntry.Create(
            user.CompanyId, clock.UtcNow, user.Subject, "auth.login", "User", user.Id.Value.ToString(), null));

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
