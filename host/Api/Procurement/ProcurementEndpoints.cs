using TrinoSupply.Foundation.Application.Iam;
using TrinoSupply.Foundation.Domain.Iam;
using TrinoSupply.Materials.Application;
using TrinoSupply.Procurement.Application;

namespace TrinoSupply.Api.Procurement;

public sealed record CreateRequisitionRequest(
    string PayingCompanyCode, string CostCenterCode, string? Priority, string Justification,
    string ApproverLevel1Subject, string ApproverLevel2Subject, IReadOnlyList<RequisitionLineInput> Lines);
public sealed record FromSuggestionsRequest(
    string PayingCompanyCode, string CostCenterCode, string? Priority, string Justification,
    string ApproverLevel1Subject, string ApproverLevel2Subject);
public sealed record AddLinesRequest(IReadOnlyList<RequisitionLineInput> Lines);
public sealed record RejectRequest(string? Note);
public sealed record CreateCostCenterRequest(string Code, string Name, string? PayingCompanyCode);
public sealed record CancelOrderRequest(string Reason);

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
            var input = new CreateRequisitionInput(req.PayingCompanyCode, req.CostCenterCode, req.Priority ?? "Normal",
                req.Justification, req.ApproverLevel1Subject, req.ApproverLevel2Subject, req.Lines);
            var r = await svc.CreateAsync(input, ct);
            return r.IsSuccess
                ? Results.Created($"/api/v1/purchases/requisitions/{r.Value}", new { requisitionId = r.Value })
                : Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        // Central de Aprovação (v2): tudo que aguarda a decisão do usuário corrente — pedidos de compra
        // na etapa dele (nível 1/2) + solicitações de almoxarifado onde ele é o gestor — no seu escopo
        // de centros de custo. Aprovar/reprovar acontecem nos endpoints de cada tipo.
        p.MapGet("/approvals", async (IPermissionChecker perm, IPurchaseRequisitionService reqs,
            TrinoSupply.Materials.Application.IStockRequestService stock, CancellationToken ct) =>
        {
            var canPurchases = await perm.HasAsync(PermissionCatalog.PurchasesApprove, ct);
            var canWarehouse = await perm.HasAsync(PermissionCatalog.WarehouseApprove, ct);
            if (!canPurchases && !canWarehouse) return Results.Forbid();

            var requisitions = canPurchases ? await reqs.ListMyApprovalsAsync(ct) : [];
            var stockRequests = canWarehouse ? await stock.ListMyApprovalsAsync(ct) : [];
            return Results.Ok(new { requisitions, stockRequests });
        }).RequireAuthorization();

        // Candidatos a aprovador (usuários com purchases.approve). Visível a quem pode requisitar,
        // para escolher os aprovadores nível 1 e nível 2 sem precisar de users.read.
        p.MapGet("/approvers", async (IPermissionChecker perm, IIamService iam, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRequest, ct)) return Results.Forbid();
            var users = await iam.ListUsersWithPermissionAsync(PermissionCatalog.PurchasesApprove, ct);
            return Results.Ok(users.Select(u => new { subject = u.Subject, displayName = u.DisplayName, email = u.Email }));
        }).RequireAuthorization();

        // ---- Centros de custo (spec Sistema de Compras) ----
        p.MapGet("/cost-centers", async (IPermissionChecker perm, ICostCenterService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListAsync(ct));
        }).RequireAuthorization();

        p.MapPost("/cost-centers", async (CreateCostCenterRequest req, IPermissionChecker perm, ICostCenterService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesOrder, ct)) return Results.Forbid();
            var r = await svc.CreateAsync(new CostCenterInput(req.Code, req.Name, req.PayingCompanyCode), ct);
            return r.IsSuccess
                ? Results.Created($"/api/v1/purchases/cost-centers/{r.Value}", new { costCenterId = r.Value })
                : Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        // Modelo (template) de planilha para cadastro de itens em lote.
        p.MapGet("/requisitions/import-template", async (IPermissionChecker perm, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRequest, ct)) return Results.Forbid();
            var bytes = RequisitionExcel.BuildTemplate();
            return Results.File(bytes, RequisitionExcel.ContentType, "modelo-itens-requisicao.xlsx");
        }).RequireAuthorization();

        // Importa itens em lote de uma planilha Excel → cria uma requisição (rascunho) com as linhas.
        p.MapPost("/requisitions/import", async (HttpRequest http, IPermissionChecker perm,
            IPurchaseRequisitionService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRequest, ct)) return Results.Forbid();
            if (!http.HasFormContentType)
                return Results.BadRequest(new { code = "purchases.import.no_file", message = "Envie a planilha no campo 'file' (multipart/form-data)." });

            var form = await http.ReadFormAsync(ct);
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { code = "purchases.import.no_file", message = "Nenhum arquivo enviado." });

            RequisitionExcel.ParseResult parsed;
            await using (var stream = file.OpenReadStream())
            {
                using var mem = new MemoryStream();
                await stream.CopyToAsync(mem, ct);
                mem.Position = 0;
                parsed = RequisitionExcel.Parse(mem);
            }

            if (parsed.Lines.Count == 0)
                return Results.BadRequest(new
                {
                    code = "purchases.import.empty",
                    message = "Nenhum item válido na planilha.",
                    errors = parsed.Errors
                });

            // Cabeçalho da solicitação vem em campos do formulário (multipart) junto com o arquivo.
            var input = new CreateRequisitionInput(
                form["payingCompanyCode"].ToString(), form["costCenterCode"].ToString(),
                form["priority"].ToString() is { Length: > 0 } pr ? pr : "Normal",
                form["justification"].ToString(), form["approverLevel1Subject"].ToString(),
                form["approverLevel2Subject"].ToString(), parsed.Lines);
            var r = await svc.CreateAsync(input, ct);
            return r.IsSuccess
                ? Results.Created($"/api/v1/purchases/requisitions/{r.Value}",
                    new { requisitionId = r.Value, imported = parsed.Lines.Count, warnings = parsed.Errors })
                : Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization().DisableAntiforgery();

        // Acrescenta itens a um rascunho: item manual na tela OU importação em lote (mesma rota).
        p.MapPost("/requisitions/{id:guid}/lines", async (Guid id, AddLinesRequest req,
            IPermissionChecker perm, IPurchaseRequisitionService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRequest, ct)) return Results.Forbid();
            return MapDecision(await svc.AddLinesAsync(id, req.Lines, ct));
        }).RequireAuthorization();

        // Ponte reposição → requisição (fecha o ciclo estoque baixo → compra). Cabeçalho no corpo.
        p.MapPost("/requisitions/from-suggestions", async (FromSuggestionsRequest req,
            IPermissionChecker perm, IReplenishmentService repl, IPurchaseRequisitionService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRequest, ct)) return Results.Forbid();
            var suggestions = await repl.GetSuggestionsAsync(ct);
            if (suggestions.Count == 0)
                return Results.BadRequest(new { code = "purchases.no_suggestions", message = "Nenhuma sugestão de reposição." });

            var lines = suggestions
                .Select(s => new RequisitionLineInput(s.ItemCode, s.SuggestedQuantity, "un"))
                .ToList();
            var input = new CreateRequisitionInput(req.PayingCompanyCode, req.CostCenterCode, req.Priority ?? "Normal",
                req.Justification, req.ApproverLevel1Subject, req.ApproverLevel2Subject, lines);
            var r = await svc.CreateAsync(input, ct);
            return r.IsSuccess
                ? Results.Created($"/api/v1/purchases/requisitions/{r.Value}", new { requisitionId = r.Value, lines = lines.Count })
                : Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        p.MapPost("/requisitions/{id:guid}/submit", async (Guid id, IPermissionChecker perm, IPurchaseRequisitionService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRequest, ct)) return Results.Forbid();
            return MapDecision(await svc.SubmitAsync(id, ct));
        }).RequireAuthorization();

        p.MapPost("/requisitions/{id:guid}/approve", async (Guid id, IPermissionChecker perm,
            IPurchaseRequisitionService svc, StockFulfillment fulfillment, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesApprove, ct)) return Results.Forbid();
            var result = await svc.ApproveAsync(id, ct);
            if (result.IsFailure) return MapDecision(result);

            // Roteamento v2: se o pedido chegou a Aprovado e o Almox tem saldo, atende pelo estoque
            // (baixa em lote atômica); senão segue a rota de compra. A resposta informa o destino.
            var route = await fulfillment.TryFulfillAsync(id, ct);
            return Results.Ok(new { route }); // "stock" = atendido pelo estoque; "purchase" = segue p/ OC
        }).RequireAuthorization();

        p.MapPost("/requisitions/{id:guid}/reject", async (Guid id, RejectRequest req, IPermissionChecker perm, IPurchaseRequisitionService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesApprove, ct)) return Results.Forbid();
            return MapDecision(await svc.RejectAsync(id, req.Note, ct));
        }).RequireAuthorization();

        // ---- Fornecedores (com dados fiscais para a OC) ----
        p.MapGet("/suppliers", async (IPermissionChecker perm, ISupplierService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListAsync(ct));
        }).RequireAuthorization();

        // Projeção de histórico por fornecedor (read model assíncrono, alimentado por eventos).
        p.MapGet("/suppliers/stats", async (IPermissionChecker perm, ISupplierService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListStatsAsync(ct));
        }).RequireAuthorization();

        p.MapGet("/suppliers/{id:guid}", async (Guid id, IPermissionChecker perm, ISupplierService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            var r = await svc.GetAsync(id, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.NotFound(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        p.MapPost("/suppliers", async (SupplierInput req, IPermissionChecker perm, ISupplierService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesOrder, ct)) return Results.Forbid();
            var r = await svc.CreateAsync(req, ct);
            return r.IsSuccess
                ? Results.Created($"/api/v1/purchases/suppliers/{r.Value}", new { supplierId = r.Value })
                : Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        p.MapPut("/suppliers/{id:guid}", async (Guid id, SupplierInput req, IPermissionChecker perm, ISupplierService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesOrder, ct)) return Results.Forbid();
            return MapDecision(await svc.UpdateAsync(id, req, ct));
        }).RequireAuthorization();

        // ---- Empresas pagadoras (registro de CNPJs do grupo; comprador da OC) ----
        p.MapGet("/paying-companies", async (IPermissionChecker perm, IPayingCompanyService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListAsync(ct));
        }).RequireAuthorization();

        p.MapGet("/paying-companies/{id:guid}", async (Guid id, IPermissionChecker perm, IPayingCompanyService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            var r = await svc.GetAsync(id, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.NotFound(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        p.MapPost("/paying-companies", async (PayingCompanyInput req, IPermissionChecker perm, IPayingCompanyService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesOrder, ct)) return Results.Forbid();
            var r = await svc.CreateAsync(req, ct);
            return r.IsSuccess
                ? Results.Created($"/api/v1/purchases/paying-companies/{r.Value}", new { payingCompanyId = r.Value })
                : Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        p.MapPut("/paying-companies/{id:guid}", async (Guid id, PayingCompanyInput req, IPermissionChecker perm, IPayingCompanyService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesOrder, ct)) return Results.Forbid();
            return MapDecision(await svc.UpdateAsync(id, req, ct));
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

        // Cancelamento de OC (com motivo). Libera a requisição para nova emissão.
        p.MapPost("/orders/{id:guid}/cancel", async (Guid id, CancelOrderRequest req,
            IPermissionChecker perm, IPurchaseOrderService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesOrder, ct)) return Results.Forbid();
            var r = await svc.CancelAsync(id, req.Reason, ct);
            if (r.IsSuccess) return Results.NoContent();
            return r.Error.Code switch
            {
                "purchases.order.not_found" => Results.NotFound(new { code = r.Error.Code, message = r.Error.Message }),
                "purchases.conflict" => Results.Conflict(new { code = r.Error.Code, message = r.Error.Message }),
                _ => Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message })
            };
        }).RequireAuthorization();

        // Download da OC em PDF (modelo do grupo Trino), preenchida com pagadora + fornecedor + preços.
        p.MapGet("/orders/{id:guid}/pdf", async (Guid id, IPermissionChecker perm,
            IPurchaseOrderService orders, IPayingCompanyService payingSvc, ISupplierService supplierSvc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();

            var orderRes = await orders.GetAsync(id, ct);
            if (orderRes.IsFailure)
                return Results.NotFound(new { code = orderRes.Error.Code, message = orderRes.Error.Message });
            var o = orderRes.Value;

            var payingRes = await payingSvc.GetAsync(o.PayingCompanyId, ct);
            var supplierRes = await supplierSvc.GetAsync(o.SupplierId, ct);
            if (payingRes.IsFailure || supplierRes.IsFailure)
                return Results.BadRequest(new { code = "purchases.order.pdf_incomplete", message = "Pagadora ou fornecedor da OC não encontrados." });

            var pay = payingRes.Value;
            var sup = supplierRes.Value;
            var data = new OcData(
                o.Number, o.IssuedAt, o.IssuedBy,
                new OcParty(pay.Code, pay.LegalName, pay.TaxId, pay.StateRegistration, pay.Address, pay.District,
                    pay.City, pay.State, pay.ZipCode, pay.Phone, pay.Email),
                new OcParty(sup.Code, sup.Name, sup.TaxId, sup.StateRegistration, sup.Address, sup.District,
                    sup.City, sup.State, sup.ZipCode, sup.Phone, sup.Email),
                o.PaymentTerms, o.PaymentMethod, o.FreightTerms,
                o.Lines.Select(l => new OcLine(
                    l.Quantity, l.Unit, l.ItemCode, l.Description, l.DeliveryDate, l.UnitPrice, l.ServiceValue,
                    l.IrrfPercent, l.IssPercent, l.IrrfValue, l.IssValue)).ToList(),
                o.ProductsValue, o.IpiValue, o.IcmsValue, o.DiscountValue, o.OtherExpenses, o.NetValue);

            var pdf = OcPdf.Build(data);
            return Results.File(pdf, "application/pdf", $"OC-{o.Number}.pdf");
        }).RequireAuthorization();

        p.MapPost("/requisitions/{id:guid}/order", async (Guid id, IssueOrderInput req,
            IPermissionChecker perm, IPurchaseOrderService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesOrder, ct)) return Results.Forbid();
            var r = await svc.IssueFromRequisitionAsync(id, req, ct);
            if (r.IsSuccess) return Results.Created($"/api/v1/purchases/orders/{r.Value}", new { orderId = r.Value });
            return r.Error.Code switch
            {
                "purchases.not_found" or "purchases.supplier.not_found" or "purchases.paying_company.not_found"
                    => Results.NotFound(new { code = r.Error.Code, message = r.Error.Message }),
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
            "purchases.sod_violation" or "purchases.wrong_approver" or "purchases.center_forbidden"
                => Results.Json(new { code = result.Error.Code, message = result.Error.Message }, statusCode: 403),
            _ => Results.BadRequest(new { code = result.Error.Code, message = result.Error.Message })
        };
    }
}
