using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;
using static TrinoSupply.Foundation.Api.Rotas.Anexos;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// SUP-001 — o fornecedor: cadastro, homologação, certidões, contrato de
/// parceria e a chave do Portal.
///
/// Saiu do Program.cs no ARQ-A. O conteúdo é o mesmo — grupo, filtros e
/// handlers na mesma ordem —, só mudou de arquivo.
/// </summary>
public static class FornecedorRotas
{
    public static void MapFornecedores(this WebApplication app)
    {
        // ---- SUP-001 — Fornecedores (MVP) --------------------------------------------
        static object SupplierView(Supplier s) => new
        {
            id = s.Id, legalName = s.LegalName, tradeName = s.TradeName, taxId = s.TaxId,
            email = s.Email, phone = s.Phone, active = s.Active,
            // homologação (V2-P2): PROSPECT participa; só HOMOLOGADO fecha processo (SUP-ERR-030)
            homologationStatus = s.HomologationStatus,
            effectiveHomologation = s.EffectiveHomologation(DateOnly.FromDateTime(DateTime.UtcNow)),
            documents = s.Documents.OrderBy(d => d.Type).Select(d => new
            {
                id = d.Id, type = d.Type, label = d.Label, documentId = d.DocumentId, fileName = d.FileName,
                validUntil = d.ValidUntil,
                expired = d.ValidUntil is not null && d.ValidUntil < DateOnly.FromDateTime(DateTime.UtcNow),
                expiringDays = d.ValidUntil is not null
                    ? (int?)(d.ValidUntil.Value.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber) : null,
                uploadedByLabel = d.UploadedByLabel,
            }),
            // contrato de parceria: produtos com preço e prazos fixos enquanto valer
            contract = new
            {
                number = s.ContractNumber, validFrom = s.ContractValidFrom, validUntil = s.ContractValidUntil,
                notes = s.ContractNotes,
                valueLimit = s.ContractValueLimit, consumed = s.ContractConsumed,
                balance = s.ContractValueLimit is not null ? s.ContractValueLimit - (s.ContractConsumed ?? 0) : null,
                current = s.ContractIsCurrent(DateOnly.FromDateTime(DateTime.UtcNow)),
                items = s.ContractItems.OrderBy(i => i.Description).Select(i => new
                {
                    id = i.Id, catalogItemId = i.CatalogItemId, catalogCode = i.CatalogCode,
                    description = i.Description, unitOfMeasure = i.UnitOfMeasure, unitPrice = i.UnitPrice,
                    paymentTerms = i.PaymentTerms, paymentDays = i.PaymentDays, deliveryDays = i.DeliveryDays,
                    notes = i.Notes,
                }),
            },
        };

        var sup = app.MapGroup("/api/v1/suppliers").RequireAuthorization();
        sup.AddEndpointFilter(RequireModules(AppModules.Fornecedores, AppModules.Compras));

        sup.MapGet("/", async (SupplierService svc, ClaimsPrincipal p, HttpContext ctx,
            bool? all, string? q, int? tamanho) =>
        {
            var role = RoleOf(p);
            if (!SupplierService.CanView(role)) return Error(ctx, 403, "SUP-ERR-900", "Seu papel não acessa fornecedores.");
            var includeInactive = all == true && SupplierService.CanMaintain(role);
            // sem `tamanho` o teto continua o de antes: os seletores de fornecedor de
            // outras telas dependem de receber a lista inteira
            var (itens, total) = await svc.BuscarAsync(includeInactive, q, tamanho ?? 500);
            return Ok(new { items = itens.Select(SupplierView), total, tamanho = itens.Count }, ctx);
        });

        sup.MapPost("/", async (CreateSupplierRequest body, SupplierService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!SupplierService.CanMaintain(RoleOf(p)))
                return Error(ctx, 403, "SUP-ERR-900", "Seu papel não mantém o cadastro de fornecedores.");
            var (supplier, error) = await svc.CreateAsync(ActorId(p), body.LegalName, body.TradeName, body.TaxId, body.Email, body.Phone);
            return error is not null ? Error(ctx, 400, error.Code, error.Message)
                : Results.Json(new { data = SupplierView(supplier!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        // contrato de parceria: vigência + produtos com preço, prazo de pagamento e entrega fixos
        sup.MapPut("/{id:guid}/contract", async (Guid id, SupplierContractRequest body, SupplierService svc,
            ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!SupplierService.CanMaintain(RoleOf(p)))
                return Error(ctx, 403, "SUP-ERR-900", "Seu papel não mantém contratos de fornecedor.");
            var items = body.Items?.Select(i => new SupplierService.ContractItemInput(
                i.CatalogItemId, i.Description, i.CatalogCode, i.UnitOfMeasure, i.UnitPrice,
                i.PaymentTerms, i.PaymentDays, i.DeliveryDays, i.Notes)).ToList();
            var (supplier, error) = await svc.SaveContractAsync(
                id, body.Number, body.ValidFrom, body.ValidUntil, body.Notes, items, body.ValueLimit);
            return error is not null ? Error(ctx, error.Code == "SUP-ERR-404" ? 404 : 422, error.Code, error.Message)
                : Ok(SupplierView(supplier!), ctx);
        });

        // pleito de reajuste do contrato (V2-P4 — Cost Avoidance): registro imutável do custo evitado
        static object AdjustmentView(ContractAdjustment a) => new
        {
            id = a.Id, requestedPercent = a.RequestedPercent, agreedPercent = a.AgreedPercent,
            baseValue = a.BaseValue, costAvoidance = a.CostAvoidance, appliedToPrices = a.AppliedToPrices,
            notes = a.Notes, createdByLabel = a.CreatedByLabel, createdAt = a.CreatedAt,
        };

        sup.MapPost("/{id:guid}/contract/adjustments", async (Guid id, ContractAdjustmentRequest body,
            SupplierService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var (adj, error) = await svc.RegisterContractAdjustmentAsync(
                actor, id, body.RequestedPercent ?? 0, body.AgreedPercent ?? 0, body.Notes, body.ApplyToPrices == true);
            return error is not null
                ? Error(ctx, error.Code switch { "SUP-ERR-404" => 404, "CT-ERR-900" => 403, _ => 422 }, error.Code, error.Message)
                : Results.Json(new { data = AdjustmentView(adj!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        sup.MapGet("/{id:guid}/contract/adjustments", async (Guid id, SupplierService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!SupplierService.CanView(RoleOf(p)))
                return Error(ctx, 403, "SUP-ERR-900", "Seu papel não acessa contratos de fornecedor.");
            var items = await svc.ContractAdjustmentsAsync(id);
            return Ok(new
            {
                items = items.Select(AdjustmentView),
                costAvoidanceTotal = items.Sum(a => a.CostAvoidance),
            }, ctx);
        });

        // homologação do fornecedor: decisão do gestor de suprimentos (V2-P2)
        sup.MapPatch("/{id:guid}/homologation", async (Guid id, HomologationRequest body, SupplierService svc,
            ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!SupplierService.CanHomologate(RoleOf(p)))
                return Error(ctx, 403, "SUP-ERR-900", "A homologação de fornecedores cabe ao gestor de suprimentos.");
            var (supplier, error) = await svc.SetHomologationAsync(id, body.Status);
            return error is not null ? Error(ctx, error.Code == "SUP-ERR-404" ? 404 : 422, error.Code, error.Message)
                : Ok(SupplierView(supplier!), ctx);
        });

        // certidões do fornecedor (multipart): arquivo + tipo + validade; vencida restringe o fornecedor
        sup.MapPost("/{id:guid}/documents", async (Guid id, HttpRequest request, AppDbContext db,
            SupplierService svc, TimeProvider clock, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!SupplierService.CanMaintain(RoleOf(p)))
                return Error(ctx, 403, "SUP-ERR-900", "Seu papel não mantém documentos de fornecedor.");
            if (!request.HasFormContentType) return Error(ctx, 400, "DOC-ERR-001", "Envie o arquivo como multipart/form-data.");
            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0) return Error(ctx, 400, "DOC-ERR-001", "Nenhum arquivo enviado.");
            if (file.Length > StoredDocument.MaxSizeBytes) return Error(ctx, 400, "DOC-ERR-002", "Arquivo acima de 10 MB.");
            if (!StoredDocument.AllowedContentTypes.Contains(file.ContentType))
                return Error(ctx, 400, "DOC-ERR-003", "Formato não permitido: envie PDF, imagem ou documento Office.");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var stored = new StoredDocument
            {
                FileName = Path.GetFileName(file.FileName), ContentType = file.ContentType, SizeBytes = file.Length,
                Content = ms.ToArray(), EntityType = "FORNECEDOR_CERTIDAO", EntityId = id,
                UploadedByLabel = p.FindFirstValue("name") ?? "Cadastro", UploadedAt = clock.GetUtcNow(),
            };
            db.StoredDocuments.Add(stored);
            await db.SaveChangesAsync();

            DateOnly? validade = DateOnly.TryParse(form["validUntil"], out var v) ? v : null;
            var (doc, error) = await svc.AddDocumentAsync(id, form["type"], form["label"], validade,
                stored.Id, stored.FileName, stored.UploadedByLabel);
            return error is not null ? Error(ctx, error.Code == "SUP-ERR-404" ? 404 : 422, error.Code, error.Message)
                : Ok(new { id = doc!.Id, documentId = stored.Id, fileName = stored.FileName }, ctx);
        }).RequireAuthorization().AddEndpointFilter(RejectSupplierRole())
            .RequireRateLimiting("upload").ComTetoDeUpload(StoredDocument.MaxRequestBytes);

        sup.MapDelete("/{id:guid}/documents/{docId:guid}", async (Guid id, Guid docId, SupplierService svc,
            ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!SupplierService.CanMaintain(RoleOf(p)))
                return Error(ctx, 403, "SUP-ERR-900", "Seu papel não mantém documentos de fornecedor.");
            var error = await svc.RemoveDocumentAsync(id, docId);
            return error is not null ? Error(ctx, 404, error.Code, error.Message) : Results.NoContent();
        });

        // gera a chave do Portal do Fornecedor (mostrada uma única vez; persiste só o hash)
        sup.MapPost("/{id:guid}/portal-key", async (Guid id, SupplierService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!SupplierService.CanMaintain(RoleOf(p)))
                return Error(ctx, 403, "SUP-ERR-900", "Seu papel não gera chaves do portal.");
            var (key, error) = await svc.GeneratePortalKeyAsync(id);
            return error is not null ? Error(ctx, 404, error.Code, error.Message)
                : Ok(new { accessKey = key, message = "Guarde a chave: ela não será exibida novamente." }, ctx);
        });

        sup.MapPatch("/{id:guid}", async (Guid id, UpdateSupplierRequest body, SupplierService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!SupplierService.CanMaintain(RoleOf(p)))
                return Error(ctx, 403, "SUP-ERR-900", "Seu papel não mantém o cadastro de fornecedores.");
            var (supplier, error) = await svc.UpdateAsync(id, body.TradeName, body.Email, body.Phone, body.Active);
            return error is not null ? Error(ctx, error.Code == "SUP-ERR-404" ? 404 : 400, error.Code, error.Message)
                : Ok(SupplierView(supplier!), ctx);
        });
    }
}
