using TrinoSupply.Foundation.Application.Iam;
using TrinoSupply.Foundation.Domain.Iam;
using TrinoSupply.Materials.Application;
using TrinoSupply.Materials.Domain;
using TrinoSupply.Procurement.Application;

namespace TrinoSupply.Api.Materials;

public sealed record CreateUnitRequest(string Code, string Name, string Dimension, decimal FactorToBase);
public sealed record CreateItemRequest(string Code, string Name, string BaseUnitCode, string? Group, string? Ca);
public sealed record PostMovementRequest(StockDirection Direction, decimal Quantity, string? Reason);
public sealed record SetPolicyRequest(decimal MinLevel, decimal MaxLevel);
public sealed record CreateCollaboratorRequest(
    string Name, string? Registration, string? CostCenterCode, string? CompanyCode, DateOnly? AdmissionDate);
public sealed record CreateStockRequestRequest(
    string CompanyCode, string CostCenterCode, string ManagerSubject, string Reason,
    IReadOnlyList<StockRequestLineInput> Lines);
public sealed record RequestNoteRequest(string? Note);

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
        m.MapGet("/items", async (string? group, int? limit, IPermissionChecker perm, IMaterialsService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListItemsAsync(group, limit ?? 500, ct));
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
            var r = await svc.CreateItemAsync(req.Code, req.Name, req.BaseUnitCode, req.Group, req.Ca, ct);
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

        // ---- Colaboradores (spec Almoxarifado) — quem recebe o EPI/fardamento na baixa ----
        m.MapGet("/collaborators", async (IPermissionChecker perm, ICollaboratorService svc, int? limit, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListAsync(limit ?? 500, ct));
        }).RequireAuthorization();

        m.MapPost("/collaborators", async (CreateCollaboratorRequest req, IPermissionChecker perm, ICollaboratorService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsManage, ct)) return Results.Forbid();
            var r = await svc.CreateAsync(new CollaboratorInput(req.Name, req.Registration, req.CostCenterCode, req.CompanyCode, req.AdmissionDate), ct);
            return r.IsSuccess
                ? Results.Created($"/api/v1/materials/collaborators/{r.Value}", new { collaboratorId = r.Value })
                : Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        // ---- Baixa de consumo (spec Almoxarifado) — entrega ao colaborador dá saída no estoque ----
        m.MapGet("/consumptions", async (IPermissionChecker perm, IConsumptionService svc, int? limit, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListAsync(limit ?? 200, ct));
        }).RequireAuthorization();

        m.MapPost("/consumptions", async (CreateConsumptionInput req, IPermissionChecker perm, IConsumptionService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsManage, ct)) return Results.Forbid();
            var r = await svc.CreateAsync(req, ct);
            return r.IsSuccess
                ? Results.Created($"/api/v1/materials/consumptions/{r.Value}", new { consumptionId = r.Value })
                : Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        // Ficha de Entrega de EPI/Uniformes (PDF) pré-preenchida a partir da baixa — para assinatura.
        m.MapGet("/consumptions/{id:guid}/ficha", async (Guid id, IPermissionChecker perm,
            IConsumptionService svc, IPayingCompanyService payingSvc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsRead, ct)) return Results.Forbid();
            var r = await svc.GetFichaAsync(id, ct);
            if (r.IsFailure) return Results.NotFound(new { code = r.Error.Code, message = r.Error.Message });
            var f = r.Value;

            // Resolve empresa (razão social + CNPJ) pelo código informado na baixa; fallback ao próprio código.
            var companies = await payingSvc.ListAsync(ct);
            var comp = companies.FirstOrDefault(p => string.Equals(p.Code, f.CompanyCode, StringComparison.OrdinalIgnoreCase));
            var data = new FichaPdfData(
                comp?.LegalName ?? f.CompanyCode, comp?.TaxId ?? "", f.CostCenterCode, f.CollaboratorName,
                f.Registration, f.AdmissionDate, f.Reason, f.IssuedBy, f.IssuedAt,
                f.Lines.Select(l => new FichaPdfLine(l.Quantity, l.Description, l.ItemCode, l.Group, l.Ca, l.DeliveredAt)).ToList());
            return Results.File(FichaPdf.Build(data), "application/pdf", "ficha-entrega-epi.pdf");
        }).RequireAuthorization();

        // ---- Solicitações de almoxarifado (Fluxo A: EPI/fardamento com fluxo de status) ----
        // Gestores aprovadores (usuários com warehouse.approve), para escolher na solicitação.
        m.MapGet("/managers", async (IPermissionChecker perm, IIamService iam, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.WarehouseRequest, ct)) return Results.Forbid();
            var users = await iam.ListUsersWithPermissionAsync(PermissionCatalog.WarehouseApprove, ct);
            return Results.Ok(users.Select(u => new { subject = u.Subject, displayName = u.DisplayName, email = u.Email }));
        }).RequireAuthorization();

        m.MapGet("/requests", async (IPermissionChecker perm, IStockRequestService svc, CancellationToken ct) =>
        {
            var manage = await perm.HasAsync(PermissionCatalog.MaterialsManage, ct);
            var canSee = manage || await perm.HasAsync(PermissionCatalog.WarehouseRequest, ct)
                || await perm.HasAsync(PermissionCatalog.WarehouseApprove, ct);
            if (!canSee) return Results.Forbid();
            // Visão do almoxarifado (todas) só para quem gerencia; senão, apenas as próprias/como gestor.
            return Results.Ok(await svc.ListAsync(all: manage, ct: ct));
        }).RequireAuthorization();

        m.MapPost("/requests", async (CreateStockRequestRequest req, IPermissionChecker perm, IStockRequestService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.WarehouseRequest, ct)) return Results.Forbid();
            var r = await svc.CreateAsync(new CreateStockRequestInput(
                req.CompanyCode, req.CostCenterCode, req.ManagerSubject, req.Reason, req.Lines), ct);
            return r.IsSuccess
                ? Results.Created($"/api/v1/materials/requests/{r.Value}", new { requestId = r.Value })
                : Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        m.MapPost("/requests/{id:guid}/approve", async (Guid id, IPermissionChecker perm, IStockRequestService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.WarehouseApprove, ct)) return Results.Forbid();
            return MapWarehouse(await svc.ApproveAsync(id, ct));
        }).RequireAuthorization();

        m.MapPost("/requests/{id:guid}/reject", async (Guid id, RequestNoteRequest body, IPermissionChecker perm, IStockRequestService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.WarehouseApprove, ct)) return Results.Forbid();
            return MapWarehouse(await svc.RejectAsync(id, body.Note, ct));
        }).RequireAuthorization();

        m.MapPost("/requests/{id:guid}/separation", async (Guid id, IPermissionChecker perm, IStockRequestService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsManage, ct)) return Results.Forbid();
            return MapWarehouse(await svc.StartSeparationAsync(id, ct));
        }).RequireAuthorization();

        m.MapPost("/requests/{id:guid}/dispatch", async (Guid id, IPermissionChecker perm, IStockRequestService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsManage, ct)) return Results.Forbid();
            return MapWarehouse(await svc.DispatchAsync(id, ct));
        }).RequireAuthorization();

        m.MapPost("/requests/{id:guid}/deliver", async (Guid id, IPermissionChecker perm, IStockRequestService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsManage, ct)) return Results.Forbid();
            return MapWarehouse(await svc.DeliverAsync(id, ct));
        }).RequireAuthorization();

        m.MapPost("/requests/{id:guid}/cancel", async (Guid id, RequestNoteRequest body, IPermissionChecker perm, IStockRequestService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsManage, ct) && !await perm.HasAsync(PermissionCatalog.WarehouseApprove, ct))
                return Results.Forbid();
            return MapWarehouse(await svc.CancelAsync(id, body.Note, ct));
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
            IPermissionChecker perm, IStockService stock, int? limit, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsRead, ct)) return Results.Forbid();
            return Results.Ok(await stock.ListMovementsAsync(code, limit ?? 200, ct));
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

    /// <summary>Mapeia o resultado das transições da solicitação de almoxarifado para HTTP.</summary>
    private static IResult MapWarehouse(TrinoSupply.BuildingBlocks.Result result)
    {
        if (result.IsSuccess) return Results.NoContent();
        return result.Error.Code switch
        {
            "warehouse.not_found" => Results.NotFound(new { code = result.Error.Code, message = result.Error.Message }),
            "warehouse.wrong_manager" or "warehouse.center_forbidden"
                => Results.Json(new { code = result.Error.Code, message = result.Error.Message }, statusCode: 403),
            "warehouse.invalid_state" => Results.Conflict(new { code = result.Error.Code, message = result.Error.Message }),
            _ => Results.BadRequest(new { code = result.Error.Code, message = result.Error.Message })
        };
    }
}
