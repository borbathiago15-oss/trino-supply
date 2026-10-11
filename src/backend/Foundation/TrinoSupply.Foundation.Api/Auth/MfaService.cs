using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Api.Auth;

/// <summary>O que a tela do autenticador precisa saber.</summary>
public record EstadoDoMfa(bool Enabled, bool Required, DateTimeOffset? EnabledAt, int RecoveryCodesLeft);

/// <summary>O cadastro começado: o segredo em claro vai uma vez à tela, para o QR e para digitar.</summary>
public record CadastroDoMfa(string Secret, string OtpauthUrl);

/// <summary>A segunda etapa confirmada no login: a sessão, e se foi por código de recuperação.</summary>
public record SegundaEtapa(AuthTokens Tokens, bool UsedRecoveryCode, int RecoveryCodesLeft);

/// <summary>
/// Cadastro, confirmação e uso do autenticador (ver <see cref="Mfa"/> para a régua).
/// </summary>
public class MfaService(AppDbContext db, TokenService tokens, CofreDeSegredos cofre, AuthService auth, TimeProvider clock)
{
    public async Task<EstadoDoMfa?> EstadoAsync(Guid userId, CancellationToken ct = default)
    {
        var u = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.Active, ct);
        return u is null ? null : new(Mfa.Ativa(u), Mfa.Exigida(u.Role), u.MfaEnabledAt, Mfa.CodigosDeRecuperacaoRestantes(u));
    }

    /// <summary>
    /// Começa (ou recomeça) o cadastro: segredo novo, gravado cifrado e ainda não válido. Com o
    /// autenticador já ativo recusa — trocar o segredo por baixo de um cadastro ativo é o que um
    /// invasor com a sessão aberta faria; desativar antes exige o código.
    /// </summary>
    public async Task<(CadastroDoMfa? cadastro, UserError? error)> IniciarAsync(Guid userId, CancellationToken ct = default)
    {
        var u = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.Active, ct);
        if (u is null) return (null, new("IAM-ERR-404", "Usuário não encontrado."));
        if (Mfa.Ativa(u))
            return (null, new("IAM-ERR-025", "A verificação em duas etapas já está ativa. Desative-a com um código antes de cadastrar outro autenticador."));

        var segredo = Totp.NovoSegredo();
        u.TotpSecret = cofre.Cifrar(segredo);
        u.MfaEnabledAt = null;
        u.MfaRecoveryCodes = null;
        u.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return (new(segredo, Totp.UrlOtpauth(Mfa.Emissor, u.Email, segredo)), null);
    }

    /// <summary>
    /// Confirma o cadastro com um código do aplicativo: liga a segunda etapa, gera os códigos
    /// de recuperação e devolve uma sessão nova — a antiga carregava a marca de "presa no
    /// cadastro", e a pessoa segue sem relogar.
    /// </summary>
    public async Task<(AuthTokens? tokens, string[]? codigos, UserError? error)> AtivarAsync(Guid userId, string? codigo, CancellationToken ct = default)
    {
        var u = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.Active, ct);
        if (u is null) return (null, null, new("IAM-ERR-404", "Usuário não encontrado."));
        if (Mfa.Ativa(u)) return (null, null, new("IAM-ERR-025", "A verificação em duas etapas já está ativa."));
        if (string.IsNullOrEmpty(u.TotpSecret))
            return (null, null, new("IAM-ERR-026", "Comece o cadastro do autenticador antes de confirmar o código."));
        var agora = clock.GetUtcNow();
        if (!Totp.Confere(cofre.Decifrar(u.TotpSecret), codigo, agora))
            return (null, null, new("IAM-ERR-027", "Código inválido. Confira o relógio do telefone e digite o código atual do aplicativo."));

        var (claros, hashes) = Mfa.NovosCodigosDeRecuperacao();
        u.MfaEnabledAt = agora;
        u.MfaRecoveryCodes = hashes;
        u.UpdatedAt = agora;
        var sessao = await auth.IssueForAsync(u, ct);
        return (sessao, claros, null);
    }

    /// <summary>Desliga a segunda etapa, com um código: só para quem o papel não obriga.</summary>
    public async Task<UserError?> DesativarAsync(Guid userId, string? codigo, CancellationToken ct = default)
    {
        var u = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.Active, ct);
        if (u is null) return new("IAM-ERR-404", "Usuário não encontrado.");
        if (Mfa.Exigida(u.Role))
            return new("IAM-ERR-028", "O seu papel exige a verificação em duas etapas: ela não pode ser desligada. Para trocar de autenticador, peça ao administrador para redefini-la.");
        if (!Mfa.Ativa(u)) return null;
        if (!Totp.Confere(cofre.Decifrar(u.TotpSecret!), codigo, clock.GetUtcNow()) && !Mfa.ConsumirCodigoDeRecuperacao(u, codigo))
            return new("IAM-ERR-027", "Código inválido.");
        Limpar(u);
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>
    /// A segunda etapa do login. O código errado conta como senha errada para o bloqueio por
    /// conta — seis dígitos sem isso seriam um milhão de tentativas livres —, e o certo zera o
    /// contador e abre a sessão. Aceita código do aplicativo ou código de recuperação.
    /// </summary>
    public async Task<(SegundaEtapa? etapa, UserError? error)> VerificarAsync(string? mfaToken, string? codigo, CancellationToken ct = default)
    {
        var agora = clock.GetUtcNow();
        var id = string.IsNullOrWhiteSpace(mfaToken) ? null : tokens.ReadMfaToken(mfaToken, agora);
        var u = id is null ? null : await db.Users.SingleOrDefaultAsync(x => x.Id == id && x.Active, ct);
        if (u is null || !Mfa.Ativa(u))
            return (null, new("IAM-ERR-029", "A etapa da senha expirou. Entre de novo com e-mail e senha."));
        if (BloqueioPorConta.Bloqueada(u, agora))
            return (null, new("IAM-ERR-003", BloqueioPorConta.Mensagem(u.LockedUntil!.Value, agora)));

        var recuperacao = false;
        var ok = Totp.Confere(cofre.Decifrar(u.TotpSecret!), codigo, agora);
        if (!ok && Mfa.PareceCodigoDeRecuperacao(codigo)) ok = recuperacao = Mfa.ConsumirCodigoDeRecuperacao(u, codigo);
        if (!ok)
        {
            BloqueioPorConta.RegistrarFalha(u, agora);
            await db.SaveChangesAsync(ct);
            return (null, new("IAM-ERR-027", "Código inválido."));
        }

        BloqueioPorConta.Desbloquear(u);
        var sessao = await auth.IssueForAsync(u, ct);
        return (new(sessao, recuperacao, Mfa.CodigosDeRecuperacaoRestantes(u)), null);
    }

    /// <summary>
    /// O administrador apaga o autenticador de alguém que perdeu o telefone e os códigos: na
    /// próxima entrada a pessoa cadastra outro (obrigatoriamente, se o papel exige). Derruba as
    /// sessões, porque a pergunta "é mesmo você?" voltou a estar aberta.
    /// </summary>
    public async Task<UserError?> RedefinirAsync(Guid userId, CancellationToken ct = default)
    {
        var u = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (u is null) return new("IAM-ERR-404", "Usuário não encontrado.");
        Limpar(u);
        var agora = clock.GetUtcNow();
        u.UpdatedAt = agora;
        foreach (var t in await db.RefreshTokens.Where(t => t.UserId == u.Id && t.RevokedAt == null).ToListAsync(ct)) t.RevokedAt = agora;
        await db.SaveChangesAsync(ct);
        return null;
    }

    private static void Limpar(User u)
    {
        u.TotpSecret = null;
        u.MfaEnabledAt = null;
        u.MfaRecoveryCodes = null;
    }
}
