using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;

namespace TrinoSupply.Foundation.Api.Auth;

public record AuthTokens(string AccessToken, int ExpiresInSeconds, string RefreshToken, User User, int RefreshTokenDays = 7);

/// <summary>
/// O resultado da tentativa de entrar. <c>Tokens</c> nulo é credencial recusada; quando a senha
/// estava certa mas a conta está fechada por tentativas erradas, <c>BloqueadaAte</c> diz até
/// quando — é o único caso em que o login explica a recusa (ver <see cref="BloqueioPorConta"/>).
/// Com a segunda etapa ligada, a senha certa não dá sessão: dá <c>MfaToken</c>, que só vale
/// para apresentar o código (ver <see cref="Mfa"/>).
/// </summary>
public record TentativaDeLogin(AuthTokens? Tokens, DateTimeOffset? BloqueadaAte, string? MfaToken = null);

public class AuthService(AppDbContext db, TokenService tokens, IPasswordHasher<User> hasher, TimeProvider clock)
{
    /// <summary>Login por e-mail e senha. Retorna null em credencial inválida (sem distinguir usuário inexistente de senha errada).</summary>
    public async Task<AuthTokens?> LoginAsync(string email, string password, CancellationToken ct = default) =>
        (await TentarEntrarAsync(email, password, ct)).Tokens;

    /// <summary>
    /// A tentativa completa, com o bloqueio por conta: a senha errada conta contra a conta e,
    /// no quinto erro seguido, a fecha por um tempo (<see cref="BloqueioPorConta"/>). A senha
    /// certa zera o contador — e, se a conta estiver fechada, é recusada dizendo até quando.
    /// </summary>
    public async Task<TentativaDeLogin> TentarEntrarAsync(string email, string password, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalized && u.Active, ct);
        if (user is null)
        {
            // o mesmo custo de PBKDF2 que a senha errada: sem isto, o tempo da resposta dizia
            // quais e-mails têm conta (auditoria A4)
            hasher.VerifyHashedPassword(new User(), HashDeReferencia(), password);
            return new(null, null);
        }

        var agora = clock.GetUtcNow();
        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            // a falha é gravada mesmo com o login recusado: é o que o bloqueio conta
            BloqueioPorConta.RegistrarFalha(user, agora);
            await db.SaveChangesAsync(ct);
            return new(null, null);
        }

        if (BloqueioPorConta.Bloqueada(user, agora))
            return new(null, user.LockedUntil);

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            user.UpdatedAt = agora;
        }

        if (Mfa.Ativa(user))
        {
            // a senha conferiu, mas o contador só zera quando o código também conferir: o
            // código errado conta como falha, e é isso que barra a adivinhação dos seis dígitos
            await db.SaveChangesAsync(ct);
            return new(null, null, tokens.CreateMfaToken(user, agora));
        }

        if (user.FailedLogins > 0 || user.LockedUntil is not null) BloqueioPorConta.Desbloquear(user);
        return new(await IssueTokensAsync(user, ct), null);
    }

    private static string? hashDeReferencia;

    /// <summary>Um hash qualquer, calculado uma vez, só para gastar o tempo da verificação.</summary>
    private string HashDeReferencia() =>
        hashDeReferencia ??= hasher.HashPassword(new User(), Guid.NewGuid().ToString("N"));

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

    /// <summary>
    /// Abre uma sessão nova para um usuário já autenticado nesta requisição —
    /// é assim que a troca de senha devolve tokens sem a marca de provisória,
    /// logo depois de revogar os antigos.
    /// </summary>
    public Task<AuthTokens> IssueForAsync(User user, CancellationToken ct = default) =>
        IssueTokensAsync(user, ct);

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
        return new AuthTokens(access, tokens.Options.AccessTokenMinutes * 60, refreshValue, user, tokens.Options.RefreshTokenDays);
    }
}
