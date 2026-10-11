using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Auth;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Users;
using static TrinoSupply.Foundation.Api.Rotas.Api;
using static TrinoSupply.Foundation.Api.Rotas.Vistas;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// Autenticação: login, refresh, quem sou, logout e a troca de senha.
///
/// É um dos dois grupos públicos do sistema — a porta de entrada. A política padrão exige
/// sessão em toda rota, então as portas (login, refresh, logout) se declaram abertas uma a
/// uma com `AllowAnonymous`, e as demais declaram a exigência explicitamente. A
/// troca de senha tem limite por usuário, e não por IP: um escritório inteiro
/// atrás do mesmo IP não pode ficar sem trocar a senha (SEC-004).
/// </summary>
public static class AutenticacaoRotas
{
    /// <param name="seedOk">
    /// Se o admin semeado pelo ambiente subiu. Vem da inicialização, e o login
    /// usa para explicar que o sistema ainda não terminou de se preparar em vez
    /// de dizer "senha inválida".
    /// </param>
    public static void MapAutenticacao(this WebApplication app, bool seedOk)
    {
        var auth = app.MapGroup("/api/v1/auth");

        auth.MapPost("/login", async (LoginRequest body, AuthService svc, TimeProvider clock, HttpContext ctx) =>
        {
            if (!seedOk)
                return Error(ctx, 503, "IAM-ERR-503",
                    "Sistema não inicializado: defina ADMIN_EMAIL e ADMIN_PASSWORD e reinicie o serviço.");
            if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password))
                return Error(ctx, 400, "IAM-ERR-400", "Informe e-mail e senha.");

            var tentativa = await svc.TentarEntrarAsync(body.Email, body.Password);
            if (tentativa.BloqueadaAte is { } ate)
                return Error(ctx, 423, "IAM-ERR-003", BloqueioPorConta.Mensagem(ate, clock.GetUtcNow()));
            // senha certa com a segunda etapa ligada: ainda não é sessão, é a vez do código
            if (tentativa.MfaToken is { } pendente)
                return Ok(new { mfaRequired = true, mfaToken = pendente, expiresIn = Mfa.MinutosDoTokenPendente * 60 }, ctx);
            return tentativa.Tokens is null
                ? Error(ctx, 401, "IAM-ERR-001", "E-mail ou senha inválidos.")
                : Ok(ToResponse(tentativa.Tokens), ctx);
        }).AllowAnonymous().RequireRateLimiting("auth");

        auth.MapPost("/refresh", async (RefreshRequest body, AuthService svc, HttpContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(body.RefreshToken))
                return Error(ctx, 400, "IAM-ERR-400", "Informe o refresh token.");
            var tokens = await svc.RefreshAsync(body.RefreshToken);
            return tokens is null
                ? Error(ctx, 401, "IAM-ERR-002", "Refresh token inválido, expirado ou revogado. Faça login novamente.")
                : Ok(ToResponse(tokens), ctx);
        }).AllowAnonymous().RequireRateLimiting("auth-refresh");

        auth.MapPost("/logout", async (RefreshRequest body, AuthService svc, HttpContext ctx) =>
        {
            if (!string.IsNullOrWhiteSpace(body.RefreshToken)) await svc.LogoutAsync(body.RefreshToken);
            return Ok(new { message = "Sessão encerrada." }, ctx);
        }).AllowAnonymous();

        auth.MapGet("/me", async (ClaimsPrincipal principal, AppDbContext db, HttpContext ctx) =>
        {
            var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("sub");
            var costCenters = Array.Empty<string>();
            var senhaProvisoria = principal.FindFirstValue(TokenService.SenhaProvisoria) == "1";
            var cadastroDoMfa = principal.FindFirstValue(Mfa.ClaimCadastro) == "1";
            var mfaAtiva = false;
            var mfaExigida = Mfa.Exigida(principal.FindFirstValue(ClaimTypes.Role) ?? "");
            if (Guid.TryParse(sub, out var uid))
            {
                var dados = await db.Users.Where(u => u.Id == uid)
                    .Select(u => new { u.CostCenters, u.MustChangePassword, u.Role, u.MfaEnabledAt, u.TotpSecret }).FirstOrDefaultAsync();
                costCenters = (dados?.CostCenters ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                // o banco manda: um token emitido antes de o admin resetar a senha não vale como quitação
                if (dados is not null)
                {
                    senhaProvisoria = dados.MustChangePassword;
                    mfaAtiva = dados.MfaEnabledAt is not null && !string.IsNullOrEmpty(dados.TotpSecret);
                    mfaExigida = Mfa.Exigida(dados.Role);
                    cadastroDoMfa = mfaExigida && !mfaAtiva;
                }
            }
            return Ok(new
            {
                id = sub,
                email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email"),
                name = principal.FindFirstValue("name"),
                role = principal.FindFirstValue(ClaimTypes.Role),
                modules = (principal.FindFirstValue("modules") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries),
                costCenters,
                mustChangePassword = senhaProvisoria,
                mfaEnabled = mfaAtiva, mfaRequired = mfaExigida, mustSetupMfa = cadastroDoMfa,
            }, ctx);
        }).RequireAuthorization();

        // ---- verificação em duas etapas (ver Auth/Mfa.cs) --------------------------------
        var mfa = auth.MapGroup("/mfa");

        // a segunda etapa do login: anônima porque a sessão ainda não existe — o que
        // identifica a pessoa é o token do meio do login, que só serve aqui
        mfa.MapPost("/verify", async (MfaVerifyRequest body, MfaService svc, HttpContext ctx) =>
        {
            var (etapa, error) = await svc.VerificarAsync(body.MfaToken, body.Code);
            if (error is not null)
                return Error(ctx, error.Code switch { "IAM-ERR-003" => 423, "IAM-ERR-029" => 401, _ => 401 }, error.Code, error.Message);
            return Ok(new { sessao = ToResponse(etapa!.Tokens), usedRecoveryCode = etapa.UsedRecoveryCode, recoveryCodesLeft = etapa.RecoveryCodesLeft }, ctx);
        }).AllowAnonymous().RequireRateLimiting("auth");

        mfa.MapGet("/", async (MfaService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var estado = await svc.EstadoAsync(ActorId(p));
            return estado is null ? Error(ctx, 404, "IAM-ERR-404", "Usuário não encontrado.") : Ok(estado, ctx);
        }).RequireAuthorization();

        mfa.MapPost("/setup", async (MfaService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var (cadastro, error) = await svc.IniciarAsync(ActorId(p));
            return error is not null
                ? Error(ctx, error.Code == "IAM-ERR-025" ? 409 : 404, error.Code, error.Message)
                : Ok(cadastro!, ctx);
        }).RequireAuthorization().RequireRateLimiting("auth-senha");

        mfa.MapPost("/enable", async (MfaCodeRequest body, MfaService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var (tokens, codigos, error) = await svc.AtivarAsync(ActorId(p), body.Code);
            if (error is not null)
                return Error(ctx, error.Code switch { "IAM-ERR-404" => 404, "IAM-ERR-025" => 409, "IAM-ERR-026" => 400, _ => 422 }, error.Code, error.Message);
            return Ok(new { sessao = ToResponse(tokens!), recoveryCodes = codigos }, ctx);
        }).RequireAuthorization().RequireRateLimiting("auth-senha");

        mfa.MapPost("/disable", async (MfaCodeRequest body, MfaService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var error = await svc.DesativarAsync(ActorId(p), body.Code);
            return error is not null
                ? Error(ctx, error.Code switch { "IAM-ERR-404" => 404, "IAM-ERR-028" => 403, _ => 422 }, error.Code, error.Message)
                : Ok(new { message = "Verificação em duas etapas desligada." }, ctx);
        }).RequireAuthorization().RequireRateLimiting("auth-senha");

        // troca de senha pelo próprio dono — a única rota que responde enquanto a senha
        // é provisória, junto de /me, /refresh e /logout (SEC-004)
        auth.MapPost("/change-password", async (ChangePasswordRequest body, UserService svc, AuthService authSvc,
            ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!Guid.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue("sub"), out var uid))
                return Error(ctx, 401, "IAM-ERR-001", "Sessão inválida. Entre novamente.");

            var (user, error) = await svc.ChangeOwnPasswordAsync(uid, body.CurrentPassword, body.NewPassword);
            if (error is not null)
                return Error(ctx, error.Code == "IAM-ERR-020" ? 401 : 422, error.Code, error.Message);

            // a troca derruba as sessões antigas, inclusive a desta aba: devolve tokens
            // novos, já sem a marca de provisória, para o usuário seguir sem relogar
            var tokens = await authSvc.IssueForAsync(user!);
            return Ok(ToResponse(tokens), ctx);
        }).RequireAuthorization().RequireRateLimiting("auth-senha");
    }
}
