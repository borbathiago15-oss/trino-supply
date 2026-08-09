using TrinoSupply.Foundation.Application.Iam;
using TrinoSupply.Foundation.Domain.Iam;

namespace TrinoSupply.Api.Iam;

public sealed record RegisterCompanyRequest(
    string LegalName, string TaxId, string AdminSubject, string AdminEmail, string AdminName);

public sealed record RegisterUserRequest(string Subject, string Email, string DisplayName);

/// <summary>Endpoints de IAM (FD-001-01). Gestão de usuários é protegida por permissão (deny-by-default).</summary>
public static class IamEndpoints
{
    public static IEndpointRouteBuilder MapIamEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v1");

        // Provisionamento de empresa (operação de PLATAFORMA). No MVP fica aberto para bootstrap;
        // em produção é restrito ao Admin de plataforma (SEC-001) — TODO(sprint 1).
        v1.MapPost("/companies", async (RegisterCompanyRequest req, IIamService iam, CancellationToken ct) =>
        {
            var result = await iam.RegisterCompanyWithAdminAsync(
                req.LegalName, req.TaxId, req.AdminSubject, req.AdminEmail, req.AdminName, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/companies/{result.Value}", new { companyId = result.Value })
                : Results.BadRequest(new { code = result.Error.Code, message = result.Error.Message });
        });

        // Lista usuários do tenant — exige users.read.
        v1.MapGet("/users", async (IPermissionChecker perm, IIamService iam, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.UsersRead, ct))
                return Results.Forbid();
            return Results.Ok(await iam.ListUsersAsync(ct));
        }).RequireAuthorization();

        // Cria usuário no tenant — exige users.manage.
        v1.MapPost("/users", async (RegisterUserRequest req, IPermissionChecker perm, IIamService iam, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.UsersManage, ct))
                return Results.Forbid();
            var result = await iam.RegisterUserAsync(req.Subject, req.Email, req.DisplayName, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/users/{result.Value}", new { userId = result.Value })
                : Results.BadRequest(new { code = result.Error.Code, message = result.Error.Message });
        }).RequireAuthorization();

        return app;
    }
}
