using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;
using static TrinoSupply.Foundation.Api.Rotas.Anexos;
using static TrinoSupply.Foundation.Api.Rotas.Api;
using static TrinoSupply.Foundation.Api.Rotas.Vistas;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// PO-001 — o pedido de compra: lista, detalhe, registro da O.C. do ERP, nota
/// fiscal, entrega e cancelamento. Junto vêm os anexos que só existem por causa
/// dele (a O.C. e a NF) e o PDF no modelo oficial.
///
/// Saiu do Program.cs no ARQ-A. O conteúdo é o mesmo — grupo, filtros e
/// handlers na mesma ordem —, só mudou de arquivo.
/// </summary>
public static class PedidoRotas
{
    public static void MapPedidos(this WebApplication app)
    {
        // ---- PO-001 — Pedido de Compra (MVP) -----------------------------------------
        var pos = app.MapGroup("/api/v1/purchase-orders").RequireAuthorization();
        pos.AddEndpointFilter(RequireModules(AppModules.Compras));

        // a busca e o filtro de situação vão para o banco: filtrar no navegador sobre uma
        // lista truncada responde "nada encontrado" para pedido que existe (PO-BR-012)
        pos.MapGet("/", async (PurchaseOrderService svc, ClaimsPrincipal p, HttpContext ctx,
            string? q, string? status, int? tamanho) =>
        {
            if (!PurchaseOrderService.CanView(RoleOf(p)))
                return Error(ctx, 403, "PO-ERR-900", "Seu papel não acessa pedidos de compra.");
            PurchaseOrderStatus? situacao = Enum.TryParse<PurchaseOrderStatus>(status, true, out var st) ? st : null;
            var (itens, total) = await svc.ListAsync(q, situacao, tamanho ?? 100);
            return Ok(new { items = itens.Select(PoView), total, tamanho = itens.Count }, ctx);
        });

        pos.MapGet("/{id:guid}", async (Guid id, PurchaseOrderService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!PurchaseOrderService.CanView(RoleOf(p)))
                return Error(ctx, 403, "PO-ERR-900", "Seu papel não acessa pedidos de compra.");
            var order = await svc.GetAsync(id);
            return order is null ? Error(ctx, 404, "PO-ERR-404", "Pedido não encontrado.") : Ok(PoView(order), ctx);
        });

        pos.MapGet("/demands", async (PurchaseOrderService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!PurchaseOrderService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "PO-ERR-900", "Seu papel não acessa as demandas de compra.");
            var (prs, mrItems) = await svc.DemandsAsync();
            return Ok(new
            {
                approvedRequisitions = prs.Select(r => new
                {
                    id = r.Id, number = r.Number, requesterLabel = r.RequesterLabel,
                    costCenter = r.CostCenter, justification = r.Justification,
                    totalEstimatedValue = r.TotalEstimatedValue, decidedAt = r.DecidedAt,
                    items = r.Items.Select(i => new
                    {
                        description = i.Description, quantity = i.Quantity,
                        unitOfMeasure = i.UnitOfMeasure, estimatedUnitPrice = i.EstimatedUnitPrice,
                        catalogCode = i.CatalogCode, catalogItemId = i.CatalogItemId,
                    }),
                }),
                purchaseRouteItems = mrItems.Select(x => new
                {
                    materialRequisitionNumber = x.mr.Number, requesterLabel = x.mr.RequesterLabel,
                    costCenter = x.mr.CostCenter, catalogItemId = x.item.CatalogItemId,
                    catalogCode = x.item.CatalogCode, description = x.item.Description,
                    unitOfMeasure = x.item.UnitOfMeasure, quantity = x.item.Quantity,
                }),
            }, ctx);
        });

        pos.MapPost("/", async (CreatePurchaseOrderRequest body, PurchaseOrderService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!PurchaseOrderService.CanManage(role))
                return Error(ctx, 403, "PO-ERR-900", "Seu papel não emite pedidos de compra.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var items = (body.Items ?? []).Select(i =>
                new PoItemInput(i.Description, i.Quantity, i.UnitOfMeasure, i.UnitPrice, i.CatalogItemId)).ToList();
            var (order, error) = await svc.CreateAsync(actor, body.SupplierId, body.Notes, items, body.SourcePrId);
            return error is not null
                ? Error(ctx, error.Code switch { "PO-ERR-021" => 422, "PO-ERR-022" => 409, _ => 400 }, error.Code, error.Message)
                : Results.Json(new { data = PoView(order!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        pos.MapPost("/{id:guid}/receive", async (Guid id, ReceiveOrderRequest body, PurchaseOrderService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!PurchaseOrderService.CanManage(role))
                return Error(ctx, 403, "PO-ERR-900", "Seu papel não registra recebimentos de pedido.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var (order, error) = await svc.ReceiveAsync(actor, id, body.LocationId);
            return error is not null
                ? Error(ctx, error.Code switch { "PO-ERR-404" => 404, "PO-ERR-040" => 409, "PO-ERR-054" => 422, _ => 400 }, error.Code, error.Message)
                : Ok(PoView(order!), ctx);
        });

        // OC feita no ERP: número, data e anexo — o sistema amarra a solicitação ao documento oficial
        pos.MapPost("/{id:guid}/erp-order", async (Guid id, ErpOrderRequest body, PurchaseOrderService svc,
            ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!PurchaseOrderService.CanManage(role))
                return Error(ctx, 403, "PO-ERR-900", "Seu papel não registra a OC do ERP.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var (order, error) = await svc.RegisterErpOrderAsync(actor, id, body.ErpNumber, body.IssuedOn, body.NoErpReason);
            return error is not null
                ? Error(ctx, error.Code switch { "PO-ERR-404" => 404, "PO-ERR-040" => 409, "PO-ERR-054" => 422, _ => 400 }, error.Code, error.Message)
                : Ok(PoView(order!), ctx);
        });

        // Faturamento: uma OC pode ter mais de uma nota fiscal
        pos.MapPost("/{id:guid}/invoices", async (Guid id, InvoiceRequest body, PurchaseOrderService svc,
            ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!PurchaseOrderService.CanManage(role))
                return Error(ctx, 403, "PO-ERR-900", "Seu papel não lança faturamento.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var (invoice, error) = await svc.AddInvoiceAsync(actor, id, body.Number, body.IssuedOn, body.Value);
            return error is not null
                ? Error(ctx, error.Code switch { "PO-ERR-404" => 404, "PO-ERR-040" => 409, "PO-ERR-054" => 422, _ => 400 }, error.Code, error.Message)
                : Results.Json(new { data = new { id = invoice!.Id, number = invoice.Number, issuedOn = invoice.IssuedOn },
                                      correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        // Confirmação de entrega: total, parcial, ou encerrando o saldo que não vai chegar
        pos.MapPost("/{id:guid}/deliveries", async (Guid id, DeliveryRequest body, PurchaseOrderService svc,
            ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!PurchaseOrderService.CanManage(role) && !CanOperateStock(p))
                return Error(ctx, 403, "PO-ERR-900", "Seu usuário não confirma entregas.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var lines = (body.Items ?? []).Select(i => new PurchaseOrderService.ReceiptLine(i.ItemId, i.Quantity, i.Rejected)).ToList();
            var (order, error) = await svc.RegisterDeliveryAsync(
                actor, id, body.LocationId, lines, body.CloseRemaining == true, body.CloseReason, body.RejectReason);
            return error is not null
                ? Error(ctx, error.Code switch { "PO-ERR-404" => 404, "PO-ERR-040" => 409, "PO-ERR-054" => 422, _ => 400 }, error.Code, error.Message)
                : Ok(PoView(order!), ctx);
        });

        pos.MapPost("/{id:guid}/cancel", async (Guid id, ReasonRequest body, PurchaseOrderService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!PurchaseOrderService.CanManage(role))
                return Error(ctx, 403, "PO-ERR-900", "Seu papel não cancela pedidos de compra.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var (order, error) = await svc.CancelAsync(actor, id, body.Reason);
            return error is not null
                ? Error(ctx, error.Code switch { "PO-ERR-404" => 404, "PO-ERR-040" => 409, "PO-ERR-054" => 422, _ => 400 }, error.Code, error.Message)
                : Ok(PoView(order!), ctx);
        });

        // ==== Anexos de OC do ERP e de nota fiscal ==================================
        app.MapPost("/api/v1/purchase-orders/{id:guid}/erp-order/attachment",
            async (Guid id, HttpRequest request, AppDbContext db, TimeProvider clock, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!PurchaseOrderService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "PO-ERR-900", "Seu papel não anexa a OC do ERP.");
            var order = await db.PurchaseOrders.SingleOrDefaultAsync(o => o.Id == id);
            if (order is null) return Error(ctx, 404, "PO-ERR-404", "Pedido não encontrado.");
            var (doc, error) = await StoreUploadAsync(request, db, clock, p, "PURCHASE_ORDER", order.Id);
            if (error is not null) return Error(ctx, 400, error.Code, error.Message);
            order.ErpDocumentId = doc!.Id;
            order.ErpFileName = doc.FileName;
            order.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync();
            return Ok(new { documentId = doc.Id, fileName = doc.FileName }, ctx);
        }).RequireAuthorization().AddEndpointFilter(RejectSupplierRole())
            .RequireRateLimiting("upload").ComTetoDeUpload(StoredDocument.MaxRequestBytes);

        app.MapPost("/api/v1/purchase-orders/{id:guid}/invoices/{invoiceId:guid}/attachment",
            async (Guid id, Guid invoiceId, HttpRequest request, AppDbContext db, TimeProvider clock,
                   ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!PurchaseOrderService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "PO-ERR-900", "Seu papel não anexa notas fiscais.");
            var invoice = await db.PurchaseOrderInvoices.SingleOrDefaultAsync(i => i.Id == invoiceId && i.OrderId == id);
            if (invoice is null) return Error(ctx, 404, "PO-ERR-404", "Nota fiscal não encontrada neste pedido.");
            var (doc, error) = await StoreUploadAsync(request, db, clock, p, "INVOICE", invoice.Id);
            if (error is not null) return Error(ctx, 400, error.Code, error.Message);
            invoice.DocumentId = doc!.Id;
            invoice.FileName = doc.FileName;
            await db.SaveChangesAsync();
            return Ok(new { documentId = doc.Id, fileName = doc.FileName }, ctx);
        }).RequireAuthorization().AddEndpointFilter(RejectSupplierRole())
            .RequireRateLimiting("upload").ComTetoDeUpload(StoredDocument.MaxRequestBytes);

        // ==== PDF da Ordem de Compra (modelo oficial) ================================
        app.MapGet("/api/v1/purchase-orders/{id:guid}/pdf", async (Guid id, AppDbContext db, QuotationService qsvc, TimeProvider clock, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!PurchaseOrderService.CanView(role) && !QuotationService.CanView(role))
                return Error(ctx, 403, "PO-ERR-900", "Seu papel não acessa a OC.");
            var order = await db.PurchaseOrders.Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == id);
            if (order is null) return Error(ctx, 404, "PO-ERR-404", "Pedido não encontrado.");
            var supplier = await db.Suppliers.SingleAsync(s => s.Id == order.SupplierId);
            var company = await db.CompanyProfiles.FirstOrDefaultAsync();
            var quotation = order.QuotationId is null ? null : await qsvc.GetAsync(order.QuotationId.Value);

            // CNPJ da empresa do CC de origem prevalece no cabeçalho (fallback: perfil padrão)
            var ccCode = quotation?.CostCenter
                ?? (order.SourcePrId is null ? null
                    : await db.Requisitions.Where(r => r.Id == order.SourcePrId).Select(r => r.CostCenter).FirstOrDefaultAsync());
            if (!string.IsNullOrWhiteSpace(ccCode))
            {
                var normalizedCc = ccCode.Trim().ToUpperInvariant();
                var companyId = await db.CostCenters.Where(c => c.Code == normalizedCc)
                    .Select(c => c.CompanyId).FirstOrDefaultAsync();
                var ccCompany = companyId is null ? null
                    : await db.Companies.SingleOrDefaultAsync(c => c.Id == companyId && c.Active);
                if (ccCompany is not null)
                    company = new CompanyProfile
                    {
                        LegalName = ccCompany.LegalName,
                        TaxId = ccCompany.TaxId.Length == 14
                            ? $"{ccCompany.TaxId[..2]}.{ccCompany.TaxId[2..5]}.{ccCompany.TaxId[5..8]}/{ccCompany.TaxId[8..12]}-{ccCompany.TaxId[12..]}"
                            : ccCompany.TaxId,
                        StateRegistration = ccCompany.StateRegistration ?? company?.StateRegistration,
                        Address = ccCompany.Address, District = ccCompany.District,
                        City = ccCompany.City, State = ccCompany.State, Zip = ccCompany.Zip,
                        Phone = ccCompany.Phone ?? company?.Phone, Email = ccCompany.Email ?? company?.Email,
                        DeliveryAddress = company?.DeliveryAddress, DeliveryTaxId = company?.DeliveryTaxId,
                        StandardClauses = company?.StandardClauses, PaymentPolicy = company?.PaymentPolicy,
                    };
            }

            var pdf = PurchaseOrderPdf.Generate(order, supplier, company, quotation);
            var doc = new StoredDocument
            {
                FileName = $"{order.Number}.pdf", ContentType = "application/pdf", SizeBytes = pdf.Length,
                Content = pdf, EntityType = "PURCHASE_ORDER_PDF", EntityId = order.Id,
                UploadedByLabel = p.FindFirstValue("name") ?? "Sistema", UploadedAt = clock.GetUtcNow(),
            };
            db.StoredDocuments.Add(doc);
            order.PdfDocumentId = doc.Id;
            if (quotation is not null)
                qsvc.RecordPdfEvent(quotation, new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role), doc.Id, order.Number);
            await db.SaveChangesAsync();
            return Results.File(pdf, "application/pdf", $"{order.Number}.pdf");
        }).RequireAuthorization().AddEndpointFilter(RejectSupplierRole());

        // O React viveu em /app/ enquanto o legado ocupava a raiz. Agora que ele é o
        // frontend, /app/... segue valendo para os links guardados e os favoritos: cada um
        // leva à mesma tela na raiz. Query e fragmento vão junto (o fragmento não chega ao
        // servidor, mas o navegador o preserva no redirecionamento).
    }
}
