using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;
using static TrinoSupply.Foundation.Api.Rotas.Anexos;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// PR-001 — a solicitação de compra: criação, edição, itens, envio, a decisão
/// do aprovador e a fila do fluxo anterior. Junto vêm os anexos da SC.
///
/// Saiu do Program.cs no ARQ-A. O conteúdo é o mesmo — grupo, filtros e
/// handlers na mesma ordem —, só mudou de arquivo.
/// </summary>
public static class SolicitacaoRotas
{
    public static void MapSolicitacoes(this WebApplication app)
    {
        // ---- PR-001 — Requisição de Compra (MVP conforme PR-001-03/13) --------------
        static object PrView(PurchaseRequisition r, ApproverHint? approver = null, ProcessStatusView? process = null,
            Acompanhamento? acompanhamento = null) => new
        {
            processStatus = process?.Key, processStatusLabel = process?.Label,
            processStatusTone = process?.Tone, processStatusHint = process?.Explanation,
            // a linha do tempo do solicitante: onde está, com quem, desde quando, quando chega
            acompanhamento = acompanhamento is null ? null : new
            {
                etapas = acompanhamento.Etapas.Select(e => new { chave = e.Chave, rotulo = e.Rotulo, situacao = e.Situacao, quando = e.Quando, quem = e.Quem }),
                etapaAtual = acompanhamento.EtapaAtual, frase = acompanhamento.Frase, comQuem = acompanhamento.ComQuem,
                desde = acompanhamento.Desde, previsao = acompanhamento.Previsao, motivo = acompanhamento.Motivo,
                precisaDoSolicitante = acompanhamento.PrecisaDoSolicitante,
                quotationId = acompanhamento.QuotationId, quotationNumber = acompanhamento.QuotationNumber,
                purchaseOrderId = acompanhamento.PurchaseOrderId, purchaseOrderNumber = acompanhamento.PurchaseOrderNumber,
                fornecedor = acompanhamento.Fornecedor,
            },
            id = r.Id,
            number = r.Number,
            kind = r.Kind,
            status = r.Status switch
            {
                RequisitionStatus.Draft => "DRAFT",
                RequisitionStatus.Submitted => "SUBMITTED",
                RequisitionStatus.InApproval => "IN_APPROVAL",
                RequisitionStatus.Approved => "APPROVED",
                RequisitionStatus.Rejected => "REJECTED",
                RequisitionStatus.Returned => "RETURNED",
                _ => "CANCELLED",
            },
            cycle = r.Cycle,
            priority = r.Priority,
            urgencyReason = r.UrgencyReason, urgencyImpact = r.UrgencyImpact,
            neededBy = r.NeededBy,
            justification = r.Justification,
            needType = r.NeedType, deliveryLocation = r.DeliveryLocation,
            company = r.Company, internalNotes = r.InternalNotes,
            budget = r.Budget,
            costCenter = r.CostCenter,
            currency = r.Currency,
            requesterId = r.RequesterId,
            requesterLabel = r.RequesterLabel,
            totalEstimatedValue = r.TotalEstimatedValue,
            decisionReason = r.DecisionReason,
            decidedByLabel = r.DecidedByLabel,
            approverLabel = approver?.ApproverLabel,
            approvalIssue = approver?.Issue,
            assignedToLabel = r.AssignedToLabel,
            decidedAt = r.DecidedAt,
            submittedAt = r.SubmittedAt,
            attachments = r.Attachments.OrderBy(a => a.UploadedAt).Select(a => new
            {
                id = a.Id, documentId = a.DocumentId, fileName = a.FileName,
                sizeBytes = a.SizeBytes, uploadedAt = a.UploadedAt, uploadedByLabel = a.UploadedByLabel,
            }),
            items = r.Items.OrderBy(i => i.Sequence).Select(i => new
            {
                itemId = i.Id, sequence = i.Sequence, description = i.Description,
                catalogCode = i.CatalogCode, catalogItemId = i.CatalogItemId, family = i.Family,
                quantity = i.Quantity, unitOfMeasure = i.UnitOfMeasure,
                estimatedUnitPrice = i.EstimatedUnitPrice,
                estimatedTotal = i.Quantity * (i.EstimatedUnitPrice ?? 0),
                notes = i.Notes,
            }),
            version = r.Version,
            createdAt = r.CreatedAt,
            updatedAt = r.UpdatedAt,
        };

        /// <summary>
        /// Situação única de cada solicitação (os oito status do fluxo de compras) e a linha do
        /// tempo do solicitante, das mesmas consultas — a etiqueta e a frase nunca discordam.
        /// </summary>
        static Task<Dictionary<Guid, (ProcessStatusView Situacao, Acompanhamento Acompanhamento)>> ProcessStatusMapAsync(
            AppDbContext db, IReadOnlyCollection<PurchaseRequisition> prs) => AcompanhamentoDaSc.MontarMapAsync(db, prs);

        static IResult PrError(HttpContext ctx, UserError e) => Error(ctx, e.Code switch
        {
            "PR-ERR-404" => 404,
            "PR-ERR-001" => 403,
            "PR-ERR-040" => 409,
            "PR-ERR-041" or "PR-ERR-042" or "PR-ERR-043" or "PR-ERR-050" => 422,
            _ => 400,
        }, e.Code, e.Message);

        var prs = app.MapGroup("/api/v1/purchase-requisitions").RequireAuthorization();
        prs.AddEndpointFilter(RequireModules(AppModules.Solicitacoes, AppModules.Aprovacao));

        // busca e paginação no servidor: procurar sobre uma lista truncada responde
        // "nada encontrado" para solicitação que existe (PO-BR-012)
        prs.MapGet("/", async (RequisitionService svc, AppDbContext db, ClaimsPrincipal p, HttpContext ctx,
            string? status, string? q, int? tamanho) =>
        {
            if (BuildActor(p) is not { } actor) return Error(ctx, 403, "PR-ERR-001", "Seu papel não acessa o módulo de requisições.");
            RequisitionStatus? filter = status?.ToUpperInvariant() switch
            {
                null or "" => null,
                "DRAFT" => RequisitionStatus.Draft,
                "IN_APPROVAL" => RequisitionStatus.InApproval,
                "APPROVED" => RequisitionStatus.Approved,
                "REJECTED" => RequisitionStatus.Rejected,
                "RETURNED" => RequisitionStatus.Returned,
                "CANCELLED" => RequisitionStatus.Cancelled,
                _ => null,
            };
            var (items, total) = await svc.ListAsync(actor, filter, q, tamanho ?? 100);
            var hints = await svc.ApproverHintsAsync(items);
            var process = await ProcessStatusMapAsync(db, items);
            return Ok(new
            {
                items = items.Select(r => PrView(r, svc.HintFor(hints, r), process.GetValueOrDefault(r.Id).Situacao, process.GetValueOrDefault(r.Id).Acompanhamento)),
                total, tamanho = items.Count,
            }, ctx);
        });

        prs.MapGet("/{id:guid}", async (Guid id, RequisitionService svc, AppDbContext db, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (BuildActor(p) is not { } actor) return Error(ctx, 403, "PR-ERR-001", "Seu papel não acessa o módulo de requisições.");
            var pr = await svc.GetAsync(actor, id);
            if (pr is null) return Error(ctx, 404, "PR-ERR-404", "Requisição não encontrada.");
            var process = await ProcessStatusMapAsync(db, [pr]);
            return Ok(PrView(pr, null, process.GetValueOrDefault(pr.Id).Situacao, process.GetValueOrDefault(pr.Id).Acompanhamento), ctx);
        });

        prs.MapPost("/", async (CreateRequisitionRequest body, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (BuildActor(p) is not { CanCreate: true } actor)
                return Error(ctx, 403, "PR-ERR-001", "Seu papel não cria requisições.");
            var items = (body.Items ?? []).Select(i => new ItemInput(i.Description ?? "", i.Quantity, i.UnitOfMeasure, i.EstimatedUnitPrice, i.Notes, i.CatalogItemId, i.Family)).ToList();
            var (pr, error) = await svc.CreateAsync(actor, body.Justification, body.CostCenter, body.Priority, body.NeededBy, items, body.Kind,
                new RequisitionService.ScHeaderInput(body.NeedType, body.DeliveryLocation, body.Company, body.InternalNotes,
                    body.UrgencyReason, body.UrgencyImpact, body.Budget));
            return error is not null ? PrError(ctx, error)
                : Results.Json(new { data = PrView(pr!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        prs.MapPatch("/{id:guid}", async (Guid id, UpdateRequisitionRequest body, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (BuildActor(p) is not { } actor) return Error(ctx, 403, "PR-ERR-001", "Seu papel não acessa o módulo de requisições.");
            var (pr, error) = await svc.UpdateHeaderAsync(actor, id, body.Justification, body.CostCenter, body.Priority, body.NeededBy,
                body.ClearNeededBy == true, body.UrgencyReason, body.UrgencyImpact,
                body.Budget, body.ClearBudget == true);
            return error is not null ? PrError(ctx, error) : Ok(PrView(pr!), ctx);
        });

        prs.MapDelete("/{id:guid}", async (Guid id, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (BuildActor(p) is not { } actor) return Error(ctx, 403, "PR-ERR-001", "Seu papel não acessa o módulo de requisições.");
            var error = await svc.DeleteDraftAsync(actor, id);
            return error is not null ? PrError(ctx, error) : Results.NoContent();
        });

        prs.MapPost("/{id:guid}/submit", async (Guid id, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (BuildActor(p) is not { } actor) return Error(ctx, 403, "PR-ERR-001", "Seu papel não acessa o módulo de requisições.");
            var (pr, error) = await svc.SubmitAsync(actor, id);
            return error is not null ? PrError(ctx, error) : Ok(PrView(pr!), ctx);
        });

        prs.MapPost("/{id:guid}/cancel", async (Guid id, ReasonRequest body, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (BuildActor(p) is not { } actor) return Error(ctx, 403, "PR-ERR-001", "Seu papel não acessa o módulo de requisições.");
            var (pr, error) = await svc.CancelAsync(actor, id, body.Reason);
            return error is not null ? PrError(ctx, error) : Ok(PrView(pr!), ctx);
        });

        prs.MapPost("/{id:guid}/items", async (Guid id, ItemRequest body, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (BuildActor(p) is not { } actor) return Error(ctx, 403, "PR-ERR-001", "Seu papel não acessa o módulo de requisições.");
            var (pr, error) = await svc.AddItemAsync(actor, id, new ItemInput(body.Description ?? "", body.Quantity, body.UnitOfMeasure, body.EstimatedUnitPrice, body.Notes, body.CatalogItemId, body.Family));
            return error is not null ? PrError(ctx, error)
                : Results.Json(new { data = PrView(pr!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        prs.MapDelete("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (BuildActor(p) is not { } actor) return Error(ctx, 403, "PR-ERR-001", "Seu papel não acessa o módulo de requisições.");
            var (pr, error) = await svc.RemoveItemAsync(actor, id, itemId);
            return error is not null ? PrError(ctx, error) : Ok(PrView(pr!), ctx);
        });

        // ---- decisão (Approver/SupplyManager/Admin, com SoD no serviço) --------------
        prs.MapPost("/{id:guid}/approve", async (Guid id, DecisionRequest body, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (BuildActor(p) is not { CanDecide: true } actor)
                return Error(ctx, 403, "PR-ERR-001", "Seu papel não aprova requisições.");
            var (pr, error) = await svc.ApproveAsync(actor, id, body.Comments);
            return error is not null ? PrError(ctx, error) : Ok(PrView(pr!), ctx);
        });

        prs.MapPost("/{id:guid}/reject", async (Guid id, DecisionRequest body, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (BuildActor(p) is not { CanDecide: true } actor)
                return Error(ctx, 403, "PR-ERR-001", "Seu papel não decide requisições.");
            var (pr, error) = await svc.RejectAsync(actor, id, body.Reason);
            return error is not null ? PrError(ctx, error) : Ok(PrView(pr!), ctx);
        });

        prs.MapPost("/{id:guid}/return", async (Guid id, DecisionRequest body, RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (BuildActor(p) is not { CanDecide: true } actor)
                return Error(ctx, 403, "PR-ERR-001", "Seu papel não decide requisições.");
            var (pr, error) = await svc.ReturnAsync(actor, id, body.Reason);
            return error is not null ? PrError(ctx, error) : Ok(PrView(pr!), ctx);
        });

        app.MapGet("/api/v1/approvals/pending", async (RequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (BuildActor(p) is not { CanDecide: true } actor)
                return Error(ctx, 403, "PR-ERR-001", "Seu papel não possui fila de aprovação.");
            var pending = await svc.PendingApprovalsAsync(actor);
            var hints = await svc.ApproverHintsAsync(pending);
            return Ok(new { items = pending.Select(r => PrView(r, svc.HintFor(hints, r))) }, ctx);
        }).RequireAuthorization().AddEndpointFilter(RequireModules(AppModules.Aprovacao));

        // ==== Anexos da solicitação de compra (PDF, imagem, planilha) ================
        app.MapPost("/api/v1/purchase-requisitions/{id:guid}/attachments",
            async (Guid id, HttpRequest request, RequisitionService svc, AppDbContext db,
                   TimeProvider clock, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var actor = BuildActor(p)!;
            var pr = await svc.GetAsync(actor, id);
            if (pr is null) return Error(ctx, 404, "PR-ERR-404", "Solicitação não encontrada.");
            if (pr.RequesterId != actor.Id && !actor.IsAdmin)
                return Error(ctx, 403, "PR-ERR-001", "Somente o titular anexa documentos à solicitação.");
            if (await svc.ChangeWindowErrorAsync(pr) is { } windowError)
                return Error(ctx, 409, windowError.Code, windowError.Message);

            if (!request.HasFormContentType) return Error(ctx, 400, "DOC-ERR-001", "Envie o arquivo como multipart/form-data.");
            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0) return Error(ctx, 400, "DOC-ERR-001", "Nenhum arquivo enviado.");
            if (file.Length > StoredDocument.MaxSizeBytes) return Error(ctx, 400, "DOC-ERR-002", "Arquivo acima de 10 MB.");
            if (!StoredDocument.AllowedContentTypes.Contains(file.ContentType))
                return Error(ctx, 400, "DOC-ERR-003", "Formato não permitido: envie PDF, planilha (XLSX/XLS/CSV), imagem ou DOCX.");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var now = clock.GetUtcNow();
            var doc = new StoredDocument
            {
                FileName = Path.GetFileName(file.FileName), ContentType = file.ContentType, SizeBytes = file.Length,
                Content = ms.ToArray(), EntityType = "REQUISITION", EntityId = pr.Id,
                UploadedByLabel = actor.Label, UploadedAt = now,
            };
            db.StoredDocuments.Add(doc);
            var attachment = new RequisitionAttachment
            {
                RequisitionId = pr.Id, DocumentId = doc.Id, FileName = doc.FileName,
                ContentType = doc.ContentType, SizeBytes = doc.SizeBytes,
                UploadedBy = actor.Id, UploadedByLabel = actor.Label, UploadedAt = now,
            };
            db.RequisitionAttachments.Add(attachment);
            await db.SaveChangesAsync();
            return Ok(new { id = attachment.Id, documentId = doc.Id, fileName = doc.FileName }, ctx);
        }).RequireAuthorization().AddEndpointFilter(RejectSupplierRole())
            .RequireRateLimiting("upload").ComTetoDeUpload(StoredDocument.MaxRequestBytes);

        app.MapDelete("/api/v1/purchase-requisitions/{id:guid}/attachments/{attachmentId:guid}",
            async (Guid id, Guid attachmentId, RequisitionService svc, AppDbContext db, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var actor = BuildActor(p)!;
            var pr = await svc.GetAsync(actor, id);
            if (pr is null) return Error(ctx, 404, "PR-ERR-404", "Solicitação não encontrada.");
            if (pr.RequesterId != actor.Id && !actor.IsAdmin)
                return Error(ctx, 403, "PR-ERR-001", "Somente o titular remove anexos da solicitação.");
            if (await svc.ChangeWindowErrorAsync(pr) is { } windowError)
                return Error(ctx, 409, windowError.Code, windowError.Message);
            var attachment = await db.RequisitionAttachments
                .SingleOrDefaultAsync(a => a.Id == attachmentId && a.RequisitionId == id);
            if (attachment is null) return Error(ctx, 404, "DOC-ERR-404", "Anexo não encontrado.");
            var doc = await db.StoredDocuments.SingleOrDefaultAsync(d => d.Id == attachment.DocumentId);
            if (doc is not null) db.StoredDocuments.Remove(doc);
            db.RequisitionAttachments.Remove(attachment);
            await db.SaveChangesAsync();
            return Ok(new { removed = true }, ctx);
        }).RequireAuthorization().AddEndpointFilter(RejectSupplierRole());

        /// <summary>Guarda um upload no cofre de documentos, com as mesmas regras de tamanho e formato.</summary>
    }
}
