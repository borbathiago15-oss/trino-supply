using TrinoSupply.Foundation.Application.Iam;
using TrinoSupply.Foundation.Domain.Iam;
using TrinoSupply.Materials.Application;
using TrinoSupply.Materials.Domain;

namespace TrinoSupply.Api.Materials;

public sealed record CreateUnitRequest(string Code, string Name, string Dimension, decimal FactorToBase);
public sealed record CreateItemRequest(string Code, string Name, string BaseUnitCode);
public sealed record PostMovementRequest(StockDirection Direction, decimal Quantity, string? Reason);

/// <summary>Endpoints de Materiais (MMS-002). Protegidos por permissão (deny-by-default).</summary>
public static class MaterialsEndpoints
{
    public static IEndpointRouteBuilder MapMaterialsEndpoints(this IEndpointRouteBuilder app)
    {
        var m = app.MapGroup("/api/v1/materials");

        // ---- Unidades de medida (ADR-013) ----
        m.MapGet("/units", async (IPermissionChecker perm, IMaterialsService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListUnitsAsync(ct));
        }).RequireAuthorization();

        m.MapPost("/units", async (CreateUnitRequest req, IPermissionChecker perm, IMaterialsService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsManage, ct)) return Results.Forbid();
            var r = await svc.CreateUnitAsync(req.Code, req.Name, req.Dimension, req.FactorToBase, ct);
            return r.IsSuccess
                ? Results.Created($"/api/v1/materials/units/{r.Value}", new { unitId = r.Value })
                : Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        // ---- Itens ----
        m.MapGet("/items", async (IPermissionChecker perm, IMaterialsService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListItemsAsync(ct));
        }).RequireAuthorization();

        m.MapPost("/items", async (CreateItemRequest req, IPermissionChecker perm, IMaterialsService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsManage, ct)) return Results.Forbid();
            var r = await svc.CreateItemAsync(req.Code, req.Name, req.BaseUnitCode, ct);
            return r.IsSuccess
                ? Results.Created($"/api/v1/materials/items/{r.Value}", new { itemId = r.Value })
                : Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        // ---- Conversão (ADR-013) ----
        m.MapGet("/convert", async (decimal quantity, string from, string to,
            IPermissionChecker perm, IMaterialsService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsRead, ct)) return Results.Forbid();
            var r = await svc.ConvertAsync(quantity, from, to, ct);
            return r.IsSuccess
                ? Results.Ok(new { quantity, from, to, result = r.Value })
                : Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        // ---- Estoque: movimentos (ledger) + saldo (projeção) ----
        m.MapPost("/items/{code}/movements", async (string code, PostMovementRequest req,
            IPermissionChecker perm, IStockService stock, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsManage, ct)) return Results.Forbid();
            var r = await stock.PostMovementAsync(code, req.Direction, req.Quantity, req.Reason, ct);
            return r.IsSuccess
                ? Results.Ok(new { itemCode = code, balance = r.Value })
                : Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        m.MapGet("/items/{code}/balance", async (string code,
            IPermissionChecker perm, IStockService stock, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsRead, ct)) return Results.Forbid();
            var r = await stock.GetBalanceAsync(code, ct);
            return r.IsSuccess
                ? Results.Ok(r.Value)
                : Results.NotFound(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        m.MapGet("/items/{code}/movements", async (string code,
            IPermissionChecker perm, IStockService stock, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsRead, ct)) return Results.Forbid();
            return Results.Ok(await stock.ListMovementsAsync(code, ct));
        }).RequireAuthorization();

        return app;
    }
}
