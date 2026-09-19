using TrinoSupply.Foundation.Application.Iam;
using TrinoSupply.Foundation.Domain.Iam;
using TrinoSupply.Materials.Application;
using TrinoSupply.Procurement.Application;

namespace TrinoSupply.Api.Procurement;

public sealed record CreateRequisitionRequest(
    string PayingCompanyCode, string CostCenterCode, string? Priority, string Justification,
    string ApproverLevel1Subject, string ApproverLevel2Subject, IReadOnlyList<RequisitionLineInput> Lines,
    DateOnly? NeededBy = null);
public sealed record FromSuggestionsRequest(
    string PayingCompanyCode, string CostCenterCode, string? Priority, string Justification,
    string ApproverLevel1Subject, string ApproverLevel2Subject);
public sealed record AddLinesRequest(IReadOnlyList<RequisitionLineInput> Lines);
public sealed record RejectRequest(string? Note);
public sealed record CreateCostCenterRequest(string Code, string Name, string? PayingCompanyCode);
public sealed record CancelOrderRequest(string Reason);
public sealed record RegisterReceiptRequest(
    string? InvoiceNumber, DateOnly? InvoiceDate, string? Notes, IReadOnlyList<ReceiptLineRequest> Lines);
public sealed record OpenQuotationRequest(
    IReadOnlyList<Guid>? RequisitionLineIds, IReadOnlyList<string> SupplierCodes,
    DateTimeOffset ClosesAt, string? Notes);
public sealed record ProposalBidRequest(Guid LineId, decimal UnitPrice, int? DeliveryDays, string? Notes);
public sealed record SubmitProposalRequest(
    string SupplierCode, string? PaymentTerms, string? FreightTerms, DateOnly? ValidUntil, string? Notes,
    IReadOnlyList<ProposalBidRequest> Bids);
public sealed record AwardLineRequest(Guid LineId, string SupplierCode, string? Note);
public sealed record AwardQuotationRequest(IReadOnlyList<AwardLineRequest> Awards);
public sealed record CancelQuotationRequest(string Reason);
public sealed record ReleaseInvoiceRequest(string? Note);

/// <summary>Endpoints de Compras (PR-001). Requisitar e aprovar são permissões distintas (SoD).</summary>
public static class ProcurementEndpoints
{
    public static IEndpointRouteBuilder MapProcurementEndpoints(this IEndpointRouteBuilder app)
    {
        var p = app.MapGroup("/api/v1/purchases");

        p.MapGet("/requisitions", async (int? limit, IPermissionChecker perm, IPurchaseRequisitionService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListAsync(limit ?? 200, ct));
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
                req.Justification, req.ApproverLevel1Subject, req.ApproverLevel2Subject, req.Lines, req.NeededBy);
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

        // Tempo de ciclo de aprovação (v3) — respeita o escopo por centro do usuário.
        p.MapGet("/analytics/cycle", async (int? days,
            IPermissionChecker perm, IPurchaseRequisitionService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.CycleStatsAsync(days ?? 90, ct));
        }).RequireAuthorization();

        // Candidatos a aprovador (usuários com purchases.approve). Visível a quem pode requisitar,
        // para escolher os aprovadores nível 1 e nível 2 sem precisar de users.read.
        p.MapGet("/approvers", async (IPermissionChecker perm, IIamService iam, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRequest, ct)) return Results.Forbid();
            var users = await iam.ListUsersWithPermissionAsync(PermissionCatalog.PurchasesApprove, ct);
            return Results.Ok(users.Select(u => new { subject = u.Subject, displayName = u.DisplayName, email = u.Email }));
        }).RequireAuthorization();

        // Saldo do Almox para os itens de um pedido (MMS-004 — evitar compra desnecessária).
        // Vive em Compras de propósito: quem REQUISITA ou APROVA precisa ver o saldo antes de decidir,
        // e normalmente não tem permissão de estoque. Orquestrado (Materiais é outro BC).
        // Item fora do catálogo do Almox volta como inCatalog=false — não é erro, é compra externa.
        p.MapGet("/stock-check", async (string? items, IPermissionChecker perm,
            TrinoSupply.Materials.Application.IStockService stock, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRequest, ct)
                && !await perm.HasAsync(PermissionCatalog.PurchasesApprove, ct)
                && !await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();

            var codigos = (items ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => c.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .Take(100)
                .ToList();

            var saldos = new List<object>(codigos.Count);
            foreach (var codigo in codigos)
            {
                var r = await stock.GetBalanceAsync(codigo, ct);
                saldos.Add(r.IsSuccess
                    ? new { itemCode = codigo, inCatalog = true, balance = r.Value.Quantity }
                    : new { itemCode = codigo, inCatalog = false, balance = 0m });
            }
            return Results.Ok(saldos);
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
        // Scorecard OTIF (Fase 03): desempenho de ENTREGA, calculado do prazo da OC × conferência na
        // doca. Complementa /suppliers/stats, que mede volume comprado, não qualidade de entrega.
        p.MapGet("/suppliers/scorecard", async (int? months, IPermissionChecker perm,
            ISupplierScorecardService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListAsync(months ?? 12, ct));
        }).RequireAuthorization();

        p.MapGet("/suppliers/{id:guid}/scorecard", async (Guid id, int? months, IPermissionChecker perm,
            ISupplierScorecardService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            var r = await svc.GetAsync(id, months ?? 12, ct);
            return r.IsSuccess
                ? Results.Ok(r.Value)
                : Results.NotFound(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

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
        p.MapGet("/orders", async (int? limit, IPermissionChecker perm, IPurchaseOrderService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListAsync(limit ?? 200, ct));
        }).RequireAuthorization();

        p.MapGet("/orders/{id:guid}", async (Guid id, IPermissionChecker perm, IPurchaseOrderService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            var r = await svc.GetAsync(id, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.NotFound(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        // Cancelamento de OC (com motivo). Libera a requisição para nova emissão.
        // ---- Recebimento de mercadoria (MMS-005) --------------------------------------------
        // Conferência da doca: pedido × entregue × avariado. A quantidade líquida entra no estoque.
        p.MapGet("/orders/{id:guid}/receipts", async (Guid id, IPermissionChecker perm,
            IGoodsReceiptService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)
                && !await perm.HasAsync(PermissionCatalog.MaterialsRead, ct)) return Results.Forbid();
            var r = await svc.GetOrderReceiptsAsync(id, ct);
            return r.IsSuccess
                ? Results.Ok(r.Value)
                : Results.NotFound(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        // Quem confere é o almoxarifado ou o comprador — qualquer um dos dois papéis serve.
        p.MapPost("/orders/{id:guid}/receipts", async (Guid id, RegisterReceiptRequest req,
            IPermissionChecker perm, IGoodsReceiptService svc,
            TrinoSupply.Api.Procurement.ReceiptStockEntry entrada, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesOrder, ct)
                && !await perm.HasAsync(PermissionCatalog.MaterialsManage, ct)) return Results.Forbid();

            var r = await svc.RegisterAsync(id, new RegisterReceiptInput(
                req.InvoiceNumber, req.InvoiceDate, req.Notes, req.Lines ?? []), ct);
            if (r.IsFailure)
                return r.Error.Code switch
                {
                    "purchases.order.not_found" => Results.NotFound(new { code = r.Error.Code, message = r.Error.Message }),
                    "purchases.conflict" => Results.Conflict(new { code = r.Error.Code, message = r.Error.Message }),
                    _ => Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message }),
                };

            // Entrada no estoque do líquido recebido (orquestrada — Materiais é outro BC).
            var posted = await entrada.PostAsync(r.Value.ReceiptId, ct);
            return Results.Created($"/api/v1/purchases/receipts/{r.Value.ReceiptId}", new
            {
                receiptId = r.Value.ReceiptId,
                orderComplete = r.Value.OrderComplete,
                hasOccurrence = r.Value.HasOccurrence,
                stockPosted = posted.Posted,
                creditedItems = posted.Credited,
                itemsNotInCatalog = posted.NotInCatalog,
                stockError = posted.Error,
            });
        }).RequireAuthorization();

        // Reprocessa a entrada de um recebimento que ficou pendente (item cadastrado depois, p.ex.).
        p.MapPost("/receipts/{id:guid}/post-stock", async (Guid id, IPermissionChecker perm,
            TrinoSupply.Api.Procurement.ReceiptStockEntry entrada, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.MaterialsManage, ct)) return Results.Forbid();
            var posted = await entrada.PostAsync(id, ct);
            return Results.Ok(new
            {
                stockPosted = posted.Posted, creditedItems = posted.Credited,
                itemsNotInCatalog = posted.NotInCatalog, stockError = posted.Error,
            });
        }).RequireAuthorization();

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
                // v3: a requisição pode render várias OCs; o conflito agora é por LINHA já pedida.
                "purchases.order.line_already_ordered" or "purchases.order.already_ordered" or "purchases.order.already_exists"
                    => Results.Conflict(new { code = r.Error.Code, message = r.Error.Message }),
                _ => Results.BadRequest(new { code = r.Error.Code, message = r.Error.Message })
            };
        }).RequireAuthorization();

        // ---- Cotação / concorrência (RFQ — Fase 03) ------------------------------------------
        // A requisição aprovada vai a mercado: propostas por item, mapa de equalização (preço ×
        // prazo × OTIF) e adjudicação por item, que desemboca nas OCs da compra dividida.
        p.MapGet("/quotations", async (int? limit, IPermissionChecker perm, IQuotationService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListAsync(limit ?? 200, ct));
        }).RequireAuthorization();

        // Mapa de equalização completo — é a tela de decisão do comprador.
        p.MapGet("/quotations/{id:guid}", async (Guid id, IPermissionChecker perm, IQuotationService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            var r = await svc.GetAsync(id, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.NotFound(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        p.MapPost("/requisitions/{id:guid}/quotation", async (Guid id, OpenQuotationRequest req,
            IPermissionChecker perm, IQuotationService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesOrder, ct)) return Results.Forbid();
            var r = await svc.OpenAsync(id, new OpenQuotationInput(
                req.RequisitionLineIds ?? [], req.SupplierCodes ?? [], req.ClosesAt, req.Notes), ct);
            return r.IsSuccess
                ? Results.Created($"/api/v1/purchases/quotations/{r.Value}", new { quotationId = r.Value })
                : MapQuotationError(r.Error);
        }).RequireAuthorization();

        // Lançamento da proposta pelo comprador (o portal do fornecedor entra por aqui depois).
        p.MapPost("/quotations/{id:guid}/proposals", async (Guid id, SubmitProposalRequest req,
            IPermissionChecker perm, IQuotationService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesOrder, ct)) return Results.Forbid();
            var r = await svc.SubmitProposalAsync(id, new SubmitProposalInput(
                req.SupplierCode, req.PaymentTerms, req.FreightTerms, req.ValidUntil, req.Notes,
                (req.Bids ?? []).Select(b => new ProposalBidInput(b.LineId, b.UnitPrice, b.DeliveryDays, b.Notes)).ToList()), ct);
            return r.IsSuccess ? Results.NoContent() : MapQuotationError(r.Error);
        }).RequireAuthorization();

        // Adjudicação por item: fora do menor preço, o domínio exige a justificativa.
        p.MapPost("/quotations/{id:guid}/award", async (Guid id, AwardQuotationRequest req,
            IPermissionChecker perm, IQuotationService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesOrder, ct)) return Results.Forbid();
            var r = await svc.AwardAsync(id, new AwardQuotationInput(
                (req.Awards ?? []).Select(a => new AwardLineInput(a.LineId, a.SupplierCode, a.Note)).ToList()), ct);
            return r.IsSuccess ? Results.NoContent() : MapQuotationError(r.Error);
        }).RequireAuthorization();

        // Fecha o ciclo: uma OC por fornecedor vencedor, no preço adjudicado e no prazo prometido.
        p.MapPost("/quotations/{id:guid}/orders", async (Guid id, IPermissionChecker perm,
            IQuotationService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesOrder, ct)) return Results.Forbid();
            var r = await svc.IssueOrdersAsync(id, ct);
            return r.IsSuccess
                ? Results.Created($"/api/v1/purchases/quotations/{id}", new { orderIds = r.Value.OrderIds })
                : MapQuotationError(r.Error);
        }).RequireAuthorization();

        p.MapPost("/quotations/{id:guid}/cancel", async (Guid id, CancelQuotationRequest req,
            IPermissionChecker perm, IQuotationService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesOrder, ct)) return Results.Forbid();
            var r = await svc.CancelAsync(id, req.Reason, ct);
            return r.IsSuccess ? Results.NoContent() : MapQuotationError(r.Error);
        }).RequireAuthorization();

        // ---- Conciliação fiscal de tres pontas (Fase 05) -------------------------------------
        // OC x NF-e x conferencia da doca. Divergencia acima da tolerancia trava o financeiro.
        p.MapGet("/orders/{id:guid}/invoices", async (Guid id, IPermissionChecker perm,
            IInvoiceMatchService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            var r = await svc.GetByOrderAsync(id, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.NotFound(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        // Ingestão do XML da NF-e (multipart, campo 'file'): nada é digitado.
        p.MapPost("/orders/{id:guid}/invoices", async (Guid id, HttpRequest http, IPermissionChecker perm,
            IInvoiceMatchService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesOrder, ct)
                && !await perm.HasAsync(PermissionCatalog.MaterialsManage, ct)) return Results.Forbid();

            string xml;
            if (http.HasFormContentType)
            {
                var form = await http.ReadFormAsync(ct);
                var file = form.Files["file"] ?? form.Files.FirstOrDefault();
                if (file is null || file.Length == 0)
                    return Results.BadRequest(new { code = "purchases.invoice.no_file", message = "Envie o XML da NF-e no campo 'file'." });
                await using var stream = file.OpenReadStream();
                using var reader = new StreamReader(stream);
                xml = await reader.ReadToEndAsync(ct);
            }
            else
            {
                // Aceita o XML cru no corpo — é assim que uma automação de caixa de entrada manda.
                using var reader = new StreamReader(http.Body);
                xml = await reader.ReadToEndAsync(ct);
            }

            var r = await svc.ImportAsync(id, xml, ct);
            return r.IsSuccess
                ? Results.Created($"/api/v1/purchases/invoices/{r.Value.InvoiceId}", r.Value)
                : MapInvoiceError(r.Error);
        }).RequireAuthorization();

        // Reprocessa a conciliação (ex.: depois de corrigir o recebimento na doca).
        p.MapPost("/invoices/{id:guid}/rematch", async (Guid id, IPermissionChecker perm,
            IInvoiceMatchService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesOrder, ct)) return Results.Forbid();
            var r = await svc.RematchAsync(id, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : MapInvoiceError(r.Error);
        }).RequireAuthorization();

        // Liberação da exceção: nota divergente só passa com justificativa, e ela vai para a auditoria.
        p.MapPost("/invoices/{id:guid}/release", async (Guid id, ReleaseInvoiceRequest req,
            IPermissionChecker perm, IInvoiceMatchService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesOrder, ct)) return Results.Forbid();
            var r = await svc.ReleaseAsync(id, req.Note, ct);
            return r.IsSuccess ? Results.NoContent() : MapInvoiceError(r.Error);
        }).RequireAuthorization();

        // ---- Compliance Score (Fase 05) ------------------------------------------------------
        // Auditoria continua: o indice aponta onde olhar, nao bloqueia nada.
        p.MapGet("/compliance", async (int? maxScore, int? limit, IPermissionChecker perm,
            IComplianceService svc, CancellationToken ct) =>
        {
            // Varredura de governanca e material de auditoria: exige a permissao de auditoria OU a
            // de compras (quem compra tem de poder ver a propria nota).
            if (!await perm.HasAsync(PermissionCatalog.AuditRead, ct)
                && !await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListAsync(maxScore, limit ?? 200, ct));
        }).RequireAuthorization();

        p.MapGet("/orders/{id:guid}/compliance", async (Guid id, IPermissionChecker perm,
            IComplianceService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            var r = await svc.GetAsync(id, ct);
            return r.IsSuccess ? Results.Ok(r.Value) : Results.NotFound(new { code = r.Error.Code, message = r.Error.Message });
        }).RequireAuthorization();

        // ---- Torre de Controlo (Fase 04) -----------------------------------------------------
        // Visao por ITEM: solicitacao -> cotacao -> OC -> recebimento -> nota, com farol de SLA.
        p.MapGet("/control-tower", async (int? days, string? costCenter, string? supplier, string? stage,
            bool? late, bool? urgent, bool? withoutOrder, string? q,
            IPermissionChecker perm, IControlTowerService svc, CancellationToken ct) =>
        {
            if (!await perm.HasAsync(PermissionCatalog.PurchasesRead, ct)) return Results.Forbid();
            return Results.Ok(await svc.ListAsync(new ControlTowerFilter(
                days ?? 90, costCenter, supplier, stage,
                late ?? false, urgent ?? false, withoutOrder ?? false, q), ct));
        }).RequireAuthorization();

        return app;
    }

    /// <summary>
    /// Erros da conciliação fiscal: nota repetida e concorrência viram 409; o resto é 400/404. O XML
    /// malformado é 400 de propósito — o problema está no arquivo enviado, não no servidor.
    /// </summary>
    private static IResult MapInvoiceError(TrinoSupply.BuildingBlocks.Error error) => error.Code switch
    {
        "purchases.order.not_found" or "purchases.invoice.not_found"
            => Results.NotFound(new { code = error.Code, message = error.Message }),
        "purchases.invoice.duplicate_key" or "purchases.conflict" or "purchases.invoice.already_released"
            => Results.Conflict(new { code = error.Code, message = error.Message }),
        _ => Results.BadRequest(new { code = error.Code, message = error.Message })
    };

    /// <summary>
    /// Erros da cotação: o que é disputa de estado (item já adjudicado, já em outra cotação) vira
    /// 409 — o cliente precisa recarregar o mapa antes de insistir; o resto é 400/404.
    /// </summary>
    private static IResult MapQuotationError(TrinoSupply.BuildingBlocks.Error error) => error.Code switch
    {
        "purchases.not_found" or "purchases.quotation.not_found" or "purchases.supplier.not_found"
            or "purchases.paying_company.not_found"
            => Results.NotFound(new { code = error.Code, message = error.Message }),
        "purchases.conflict" or "purchases.quotation.line_already_awarded"
            or "purchases.quotation.line_already_ordered" or "purchases.quotation.line_in_open_quotation"
            or "purchases.quotation.already_awarded" or "purchases.order.line_already_ordered"
            => Results.Conflict(new { code = error.Code, message = error.Message }),
        _ => Results.BadRequest(new { code = error.Code, message = error.Message })
    };

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
