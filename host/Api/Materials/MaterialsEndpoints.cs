using TrinoSupply.Foundation.Application.Iam;
using TrinoSupply.Foundation.Domain.Iam;
using TrinoSupply.Materials.Application;
using TrinoSupply.Materials.Domain;

namespace TrinoSupply.Api.Materials;

public sealed record CreateUnitRequest(string Code, string Name, string Dimension, decimal FactorToBase);
public sealed record CreateItemRequest(string Code, string Name, string BaseUnitCode, string? Group);
public sealed record PostMovementRequest(StockDirection Direction, decimal Quantity, string? Reason);
public sealed record SetPolicyRequest(decimal MinLevel, decimal MaxLevel);

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

        // ---- Itens (com grupo/família — spec Sistema de Compras) ----
        m.MapGet("/items", async (string? group, IPermissionChecker perm, IMaterialsService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListItemsAsync(group, ct));
        }).RequireAuthorization();

        // Catálogo de grupos/famílias de produto (alimenta filtros e a solicitação em lote por família).
        m.MapGet("/product-groups", async (IPermissionChecker perm, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsRead, ct)) return Results.Forbid();
            return Results.Ok(ProductGroups.All);
        }).RequireAuthorization();

        m.MapPost("/items", async (CreateItemRequest req, IPermissionChecker perm, IMaterialsService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsManage, ct)) return Results.Forbid();
            var r = await svc.CreateItemAsync(req.Code, req.Name, req.BaseUnitCode, req.Group, ct);
            return r.IsSuccess
                ? Results.Created($"/api/v1/materials/items/{r.Value}", new { itemId = r.Value })
                : Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        // Modelo (template) de planilha para cadastro de itens em lote.
        m.MapGet("/items/import-template", async (IPermissionChecker perm, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsManage, ct)) return Results.Forbid();
            return Results.File(ItemExcel.BuildTemplate(), ItemExcel.ContentType, "modelo-itens-produtos.xlsx");
        }).RequireAuthorization();

        // Importa itens/produtos em lote de uma planilha (código, descrição, unidade, grupo).
        m.MapPost("/items/import", async (HttpRequest http, IPermissionChecker perm, IMaterialsService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsManage, ct)) return Results.Forbid();
            if (!http.HasFormContentType)
                return Results.BadRequest(new { code = "materials.import.no_file", message = "Envie a planilha no campo 'file'." });

            var form = await http.ReadFormAsync(ct);
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { code = "materials.import.no_file", message = "Nenhum arquivo enviado." });

            IReadOnlyList<ItemImportRow> rows;
            IReadOnlyList<string> parseErrors;
            await using (var stream = file.OpenReadStream())
            {
                using var mem = new MemoryStream();
                await stream.CopyToAsync(mem, ct);
                mem.Position = 0;
                (rows, parseErrors) = ItemExcel.Parse(mem);
            }

            if (rows.Count == 0)
                return Results.BadRequest(new { code = "materials.import.empty", message = "Nenhum item válido na planilha.", errors = parseErrors });

            var result = await svc.ImportItemsAsync(rows, ct);
            var warnings = parseErrors.Concat(result.Errors).ToList();
            return Results.Ok(new { imported = result.Imported, warnings });
        }).RequireAuthorization().DisableAntiforgery();

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

        // ---- Reposição (ADR-014) ----
        m.MapPut("/items/{code}/replenishment", async (string code, SetPolicyRequest req,
            IPermissionChecker perm, IReplenishmentService repl, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsManage, ct)) return Results.Forbid();
            var r = await repl.SetPolicyAsync(code, req.MinLevel, req.MaxLevel, ct);
            if (r.IsSuccess) return Results.NoContent();
            return r.Error.Code.EndsWith("not_found")
                ? Results.NotFound(new { code = r.Error.Code, message = r.Error.Message })
                : Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        m.MapGet("/items/{code}/replenishment", async (string code,
            IPermissionChecker perm, IReplenishmentService repl, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsRead, ct)) return Results.Forbid();
            var r = await repl.GetPolicyAsync(code, ct);
            return r.IsSuccess
                ? Results.Ok(r.Value)
                : Results.NotFound(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        m.MapGet("/replenishment/suggestions", async (
            IPermissionChecker perm, IReplenishmentService repl, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsRead, ct)) return Results.Forbid();
            return Results.Ok(await repl.GetSuggestionsAsync(ct));
        }).RequireAuthorization();

        return app;
    }
}
