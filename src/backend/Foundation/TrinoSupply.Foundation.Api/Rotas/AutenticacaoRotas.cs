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
/// É um dos dois grupos públicos do sistema — a porta de entrada — e por isso
/// as rotas autenticadas de dentro dele declaram a exigência uma a uma. A
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

        auth.MapPost("/login", async (LoginRequest body, AuthService svc, HttpContext ctx) =>
        {
            if (!seedOk)
                return Error(ctx, 503, "IAM-ERR-503",
                    "Sistema não inicializado: defina ADMIN_EMAIL e ADMIN_PASSWORD e reinicie o serviço.");
            if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password))
                return Error(ctx, 400, "IAM-ERR-400", "Informe e-mail e senha.");

            var tokens = await svc.LoginAsync(body.Email, body.Password);
            return tokens is null
                ? Error(ctx, 401, "IAM-ERR-001", "E-mail ou senha inválidos.")
                : Ok(ToResponse(tokens), ctx);
        }).RequireRateLimiting("auth");

        auth.MapPost("/refresh", async (RefreshRequest body, AuthService svc, HttpContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(body.RefreshToken))
                return Error(ctx, 400, "IAM-ERR-400", "Informe o refresh token.");
            var tokens = await svc.RefreshAsync(body.RefreshToken);
            return tokens is null
                ? Error(ctx, 401, "IAM-ERR-002", "Refresh token inválido, expirado ou revogado. Faça login novamente.")
                : Ok(ToResponse(tokens), ctx);
        }).RequireRateLimiting("auth-refresh");

        auth.MapPost("/logout", async (RefreshRequest body, AuthService svc, HttpContext ctx) =>
        {
            if (!string.IsNullOrWhiteSpace(body.RefreshToken)) await svc.LogoutAsync(body.RefreshToken);
            return Ok(new { message = "Sessão encerrada." }, ctx);
        });

        auth.MapGet("/me", async (ClaimsPrincipal principal, AppDbContext db, HttpContext ctx) =>
        {
            var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("sub");
            var costCenters = Array.Empty<string>();
            var senhaProvisoria = principal.FindFirstValue(TokenService.SenhaProvisoria) == "1";
            if (Guid.TryParse(sub, out var uid))
            {
                var dados = await db.Users.Where(u => u.Id == uid)
                    .Select(u => new { u.CostCenters, u.MustChangePassword }).FirstOrDefaultAsync();
                costCenters = (dados?.CostCenters ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                // o banco manda: um token emitido antes de o admin resetar a senha não vale como quitação
                if (dados is not null) senhaProvisoria = dados.MustChangePassword;
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
            }, ctx);
        }).RequireAuthorization();

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
