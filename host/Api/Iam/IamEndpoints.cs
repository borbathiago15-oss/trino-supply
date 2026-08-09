using TrinoSupply.Foundation.Application.Iam;
using TrinoSupply.Foundation.Domain.Iam;

namespace TrinoSupply.Api.Iam;

public sealed record RegisterCompanyRequest(
    string LegalName, string TaxId, string AdminSubject, string AdminEmail, string AdminName);

public sealed record RegisterUserRequest(string Subject, string Email, string DisplayName);
public sealed record CreateRoleRequest(string Name);
public sealed record PermissionRequest(string Permission);
public sealed record AssignRoleRequest(Guid RoleId);

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

        // ---- Papéis ----

        v1.MapGet("/roles", async (IPermissionChecker perm, IIamService iam, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.RolesRead, ct)) return Results.Forbid();
            return Results.Ok(await iam.ListRolesAsync(ct));
        }).RequireAuthorization();

        v1.MapPost("/roles", async (CreateRoleRequest req, IPermissionChecker perm, IIamService iam, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.RolesManage, ct)) return Results.Forbid();
            var result = await iam.CreateRoleAsync(req.Name, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/roles/{result.Value}", new { roleId = result.Value })
                : Results.BadRequest(new { code = result.Error.Code, message = result.Error.Message });
        }).RequireAuthorization();

        v1.MapPost("/roles/{roleId:guid}/permissions", async (Guid roleId, PermissionRequest req,
            IPermissionChecker perm, IIamService iam, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.RolesManage, ct)) return Results.Forbid();
            return Map(await iam.GrantPermissionAsync(roleId, req.Permission, ct));
        }).RequireAuthorization();

        v1.MapDelete("/roles/{roleId:guid}/permissions/{permission}", async (Guid roleId, string permission,
            IPermissionChecker perm, IIamService iam, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.RolesManage, ct)) return Results.Forbid();
            return Map(await iam.RevokePermissionAsync(roleId, permission, ct));
        }).RequireAuthorization();

        // ---- Atribuição de papéis a usuários ----

        v1.MapPost("/users/{userId:guid}/roles", async (Guid userId, AssignRoleRequest req,
            IPermissionChecker perm, IIamService iam, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.UsersManage, ct)) return Results.Forbid();
            return Map(await iam.AssignRoleToUserAsync(userId, req.RoleId, ct));
        }).RequireAuthorization();

        v1.MapDelete("/users/{userId:guid}/roles/{roleId:guid}", async (Guid userId, Guid roleId,
            IPermissionChecker perm, IIamService iam, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.UsersManage, ct)) return Results.Forbid();
            return Map(await iam.RemoveRoleFromUserAsync(userId, roleId, ct));
        }).RequireAuthorization();

        // ---- Auditoria (append-only, somente leitura) ----

        v1.MapGet("/audit", async (int? limit, IPermissionChecker perm, IIamService iam, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.AuditRead, ct)) return Results.Forbid();
            return Results.Ok(await iam.ListAuditAsync(limit ?? 100, ct));
        }).RequireAuthorization();

        return app;
    }

    /// <summary>Traduz um <see cref="Result"/> de negócio para HTTP (404 para "não encontrado", 400 senão).</summary>
    private static IResult Map(TrinoSupply.BuildingBlocks.Result result)
    {
        if (result.IsSuccess) return Results.NoContent();
        var code = result.Error.Code;
        return code.EndsWith("not_found")
            ? Results.NotFound(new { code, message = result.Error.Message })
            : Results.BadRequest(new { code, message = result.Error.Message });
    }
}
