using TrinoSupply.Foundation.Application.Iam;
using TrinoSupply.Foundation.Domain.Iam;
using TrinoSupply.Materials.Application;
using TrinoSupply.Procurement.Application;

namespace TrinoSupply.Api.Procurement;

public sealed record CreateRequisitionRequest(IReadOnlyList<RequisitionLineInput> Lines);
public sealed record RejectRequest(string? Note);
public sealed record CreateSupplierRequest(string Code, string Name, string TaxId);
public sealed record IssueOrderRequest(string SupplierCode);

/// <summary>Endpoints de Compras (PR-001). Requisitar e aprovar são permissões distintas (SoD).</summary>
public static class ProcurementEndpoints
{
    public static IEndpointRouteBuilder MapProcurementEndpoints(this IEndpointRouteBuilder app)
    {
        var p = app.MapGroup("/api/v1/purchases");

        p.MapGet("/requisitions", async (IPermissionChecker perm, IPurchaseRequisitionService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListAsync(ct));
        }).RequireAuthorization();

        p.MapGet("/requisitions/{id:guid}", async (Guid id, IPermissionChecker perm, IPurchaseRequisitionService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            var r = await svc.GetAsync(id, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.NotFound(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        p.MapPost("/requisitions", async (CreateRequisitionRequest req, IPermissionChecker perm, IPurchaseRequisitionService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRequest, ct)) return Results.Forbid();
            var r = await svc.CreateAsync(req.Lines, ct);
            return r.IsSuccess
                ? Results.Created($"/api/v1/purchases/requisitions/{r.Value}", new { requisitionId = r.Value })
                : Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        // Ponte reposição → requisição (fecha o ciclo estoque baixo → compra).
        p.MapPost("/requisitions/from-suggestions", async (
            IPermissionChecker perm, IReplenishmentService repl, IPurchaseRequisitionService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRequest, ct)) return Results.Forbid();
            var suggestions = await repl.GetSuggestionsAsync(ct);
            if (suggestions.Count == 0)
                return Results.BadRequest(new { code = "purchases.no_suggestions", message = "Nenhuma sugestão de reposição." });

            var lines = suggestions
                .Select(s => new RequisitionLineInput(s.ItemCode, s.SuggestedQuantity, "un"))
                .ToList();
            var r = await svc.CreateAsync(lines, ct);
            return r.IsSuccess
                ? Results.Created($"/api/v1/purchases/requisitions/{r.Value}", new { requisitionId = r.Value, lines = lines.Count })
                : Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        p.MapPost("/requisitions/{id:guid}/submit", async (Guid id, IPermissionChecker perm, IPurchaseRequisitionService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRequest, ct)) return Results.Forbid();
            return MapDecision(await svc.SubmitAsync(id, ct));
        }).RequireAuthorization();

        p.MapPost("/requisitions/{id:guid}/approve", async (Guid id, IPermissionChecker perm, IPurchaseRequisitionService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesApprove, ct)) return Results.Forbid();
            return MapDecision(await svc.ApproveAsync(id, ct));
        }).RequireAuthorization();

        p.MapPost("/requisitions/{id:guid}/reject", async (Guid id, RejectRequest req, IPermissionChecker perm, IPurchaseRequisitionService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesApprove, ct)) return Results.Forbid();
            return MapDecision(await svc.RejectAsync(id, req.Note, ct));
        }).RequireAuthorization();

        // ---- Fornecedores ----
        p.MapGet("/suppliers", async (IPermissionChecker perm, ISupplierService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListAsync(ct));
        }).RequireAuthorization();

        p.MapPost("/suppliers", async (CreateSupplierRequest req, IPermissionChecker perm, ISupplierService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesOrder, ct)) return Results.Forbid();
            var r = await svc.CreateAsync(req.Code, req.Name, req.TaxId, ct);
            return r.IsSuccess
                ? Results.Created($"/api/v1/purchases/suppliers/{r.Value}", new { supplierId = r.Value })
                : Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        // ---- Pedidos de compra (emitidos de requisição aprovada) ----
        p.MapGet("/orders", async (IPermissionChecker perm, IPurchaseOrderService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListAsync(ct));
        }).RequireAuthorization();

        p.MapGet("/orders/{id:guid}", async (Guid id, IPermissionChecker perm, IPurchaseOrderService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            var r = await svc.GetAsync(id, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.NotFound(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        p.MapPost("/requisitions/{id:guid}/order", async (Guid id, IssueOrderRequest req,
            IPermissionChecker perm, IPurchaseOrderService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesOrder, ct)) return Results.Forbid();
            var r = await svc.IssueFromRequisitionAsync(id, req.SupplierCode, ct);
            if (r.IsSuccess) return Results.Created($"/api/v1/purchases/orders/{r.Value}", new { orderId = r.Value });
            return r.Error.Code switch
            {
                "purchases.not_found" or "purchases.supplier.not_found" => Results.NotFound(new { code = r.Error.Code, message = r.Error.Message }),
                "purchases.order.already_exists" => Results.Conflict(new { code = r.Error.Code, message = r.Error.Message }),
                _ => Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message })
            };
        }).RequireAuthorization();

        return app;
    }

    private static IResult MapDecision(TrinoSupply.BuildingBlocks.Result result)
    {
        if (result.IsSuccess) return Results.NoContent();
        return result.Error.Code switch
        {
            "purchases.not_found" => Results.NotFound(new { code = result.Error.Code, message = result.Error.Message }),
            "purchases.conflict" => Results.Conflict(new { code = result.Error.Code, message = result.Error.Message }),
            "purchases.sod_violation" => Results.Json(new { code = result.Error.Code, message = result.Error.Message }, statusCode: 403),
            _ => Results.BadRequest(new { code = result.Error.Code, message = result.Error.Message })
        };
    }
}
