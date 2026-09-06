using System.Security.Claims;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Users;
using static TrinoSupply.Foundation.Api.Rotas.Anexos;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// Gestão de usuários: cadastro, papel, módulos e redefinição de senha. Só o
/// administrador alcança, e é o grupo que se protege com RequireRole em vez
/// de filtro de módulo.
///
/// Saiu do Program.cs no ARQ-A. O conteúdo é o mesmo — grupo, filtros e
/// handlers na mesma ordem —, só mudou de arquivo.
/// </summary>
public static class UsuarioRotas
{
    public static void MapUsuarios(this WebApplication app)
    {
        // ---- Gestão de usuários (exclusiva do SystemAdministrator) -------------------
        var users = app.MapGroup("/api/v1/users")
            .RequireAuthorization(p => p.RequireRole(Roles.SystemAdministrator));

        static object UserView(User u) => new
        {
            id = u.Id, email = u.Email, name = u.Name, role = u.Role, active = u.Active,
            modules = AppModules.EffectiveFor(u), customModules = u.Modules is not null,
            costCenters = (u.CostCenters ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            directorId = u.DirectorId,
            // quem ainda não definiu a própria senha aparece marcado na lista do admin
            mustChangePassword = u.MustChangePassword, passwordChangedAt = u.PasswordChangedAt,
            createdAt = u.CreatedAt, updatedAt = u.UpdatedAt,
        };

        users.MapGet("/", async (UserService svc, HttpContext ctx) =>
            Ok(new { items = (await svc.ListAsync()).Select(UserView), roles = UserService.ValidRoles, availableModules = AppModules.All }, ctx));

        users.MapPost("/", async (CreateUserRequest body, UserService svc, HttpContext ctx) =>
        {
            var (user, error) = await svc.CreateAsync(body.Email, body.Name, body.Role, body.Password,
                body.Modules, body.CostCenters, body.DirectorId);
            return error is not null
                ? Error(ctx, error.Code switch { "IAM-ERR-014" => 409, "IAM-ERR-021" => 422, _ => 400 }, error.Code, error.Message)
                : Results.Json(new { data = UserView(user!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        users.MapPatch("/{id:guid}", async (Guid id, UpdateUserRequest body, UserService svc, ClaimsPrincipal principal, HttpContext ctx) =>
        {
            var (user, error) = await svc.UpdateAsync(id, ActorId(principal), body.Name, body.Role, body.Active,
                body.Modules, body.CostCenters, body.DirectorId, body.ClearDirector == true);
            return error is not null
                ? Error(ctx, error.Code == "IAM-ERR-404" ? 404 : 422, error.Code, error.Message)
                : Ok(UserView(user!), ctx);
        });

        users.MapPost("/{id:guid}/reset-password", async (Guid id, ResetPasswordRequest body, UserService svc, HttpContext ctx) =>
        {
            var error = await svc.ResetPasswordAsync(id, body.NewPassword);
            return error is not null
                ? Error(ctx, error.Code == "IAM-ERR-404" ? 404 : 400, error.Code, error.Message)
                : Ok(new { message = "Senha redefinida. As sessões do usuário foram encerradas." }, ctx);
        });
    }
}
