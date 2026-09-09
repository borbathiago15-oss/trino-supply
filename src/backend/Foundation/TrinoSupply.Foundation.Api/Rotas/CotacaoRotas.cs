using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Auth;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;
using static TrinoSupply.Foundation.Api.Rotas.Api;
using static TrinoSupply.Foundation.Api.Rotas.Vistas;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// RFQ-001 — o processo fechado de compras: cotação, propostas, negociação,
/// escolha do vencedor, as duas aprovações e o registro da O.C. do ERP. Junto
/// vem o Portal do Fornecedor, que é o outro lado do mesmo processo e publica
/// as mesmas vistas.
///
/// Saiu do Program.cs no ARQ-A. O conteúdo é o mesmo — grupos, filtros e
/// handlers na mesma ordem —, só mudou de arquivo.
/// </summary>
public static class CotacaoRotas
{
    public static void MapCotacoes(this WebApplication app)
    {
        // ==== RFQ-001 — Processo fechado de compras (cotação → aprovações → OC) ======
        static object ProposalView(Proposal p, Quotation q) => new
        {
            id = p.Id, supplierId = p.SupplierId, supplierName = p.SupplierName,
            version = p.VersionNumber, totalValue = p.TotalValue, deliveryDays = p.DeliveryDays,
            paymentTerms = p.PaymentTerms, paymentMethodName = p.PaymentMethodName,
            paymentDays = p.PaymentDays,
            freightValue = p.FreightValue, taxValue = p.TaxValue, otherCosts = p.OtherCosts,
            validUntil = p.ValidUntil,
            discountValue = p.DiscountValue, currency = p.Currency,
            notes = p.Notes, submittedVia = p.SubmittedVia, submittedByLabel = p.SubmittedByLabel,
            submittedAt = p.SubmittedAt, attachmentDocumentId = p.AttachmentDocumentId,
            attachmentFileName = p.AttachmentFileName,
            isLatest = q.Proposals.Where(x => x.SupplierId == p.SupplierId).Max(x => x.VersionNumber) == p.VersionNumber,
            isWinner = q.WinnerProposalId == p.Id,
            items = p.Items.Select(i => new { quotationItemId = i.QuotationItemId, unitPrice = i.UnitPrice, quantity = i.Quantity }),
        };

        static object QuotationView(Quotation q) => new
        {
            id = q.Id, number = q.Number, kind = QKindLabel(q.Kind), status = QStatusLabel(q.Status),
            sourcePrId = q.SourcePrId, sourcePrNumber = q.SourcePrNumber, costCenter = q.CostCenter,
            sourcePrNumbers = q.SourcePrNumbers,
            justification = q.Justification, deadline = q.Deadline, notes = q.Notes,
            createdByLabel = q.CreatedByLabel, createdAt = q.CreatedAt, decisionReason = q.DecisionReason,
            items = q.Items.OrderBy(i => i.Sequence).Select(i => new
            {
                id = i.Id, sequence = i.Sequence, catalogItemId = i.CatalogItemId, catalogCode = i.CatalogCode,
                description = i.Description, quantity = i.Quantity, unitOfMeasure = i.UnitOfMeasure,
                sourcePrNumber = i.SourcePrNumber ?? q.SourcePrNumber,
                family = QuotationAward.FamilyKey(i.Family),
            }),
            families = q.Families,
            suppliers = q.Suppliers.Select(s => new
            {
                supplierId = s.SupplierId, supplierName = s.SupplierName, taxId = s.TaxId,
                invitedAt = s.InvitedAt, invitedByLabel = s.InvitedByLabel,
                hasProposal = q.Proposals.Any(p => p.SupplierId == s.SupplierId),
            }),
            proposals = q.Proposals.OrderBy(p => p.SupplierName).ThenByDescending(p => p.VersionNumber)
                .Select(p => ProposalView(p, q)),
            selection = q.SelectedAt is null ? null : new
            {
                winnerSupplierId = q.WinnerSupplierId, winnerProposalId = q.WinnerProposalId,
                criteria = q.SelectionCriteria, justification = q.SelectionJustification,
                // o id de quem agiu vai junto do rótulo: é com ele que a tela aplica a
                // segregação de funções (RFQ-ERR-030) antes de oferecer o botão de aprovar
                by = q.SelectedBy, byLabel = q.SelectedByLabel, at = q.SelectedAt,
            },
            managerApproval = q.ManagerApprovedAt is null ? null
                : new { by = q.ManagerApprovedBy, byLabel = q.ManagerApprovedByLabel, at = q.ManagerApprovedAt },
            directorApproval = q.DirectorApprovedAt is null ? null
                : new { by = q.DirectorApprovedBy, byLabel = q.DirectorApprovedByLabel, at = q.DirectorApprovedAt },
            // adjudicação por família: a mesma compra pode ficar com vários fornecedores, um por família
            awards = q.AwardList.Select(a => new
            {
                id = a.Id, family = a.Family, supplierId = a.SupplierId, supplierName = a.SupplierName,
                proposalId = a.ProposalId, proposalVersion = a.ProposalVersion,
                itemsValue = a.ItemsValue, totalValue = a.TotalValue,
                criteria = a.Criteria, justification = a.Justification,
                byLabel = a.SelectedByLabel, at = a.SelectedAt,
                purchaseOrderId = a.PurchaseOrderId, purchaseOrderNumber = a.PurchaseOrderNumber,
            }),
            splitAward = q.IsSplitAward,
            // fornecedores adjudicados que ainda não tiveram a O.C. registrada
            pendingPoSuppliers = q.AwardList.Where(a => a.PurchaseOrderId is null)
                .GroupBy(a => new { a.SupplierId, a.SupplierName })
                .Select(g => new
                {
                    supplierId = g.Key.SupplierId, supplierName = g.Key.SupplierName,
                    families = g.Select(a => a.Family).OrderBy(f => f).ToList(),
                    totalValue = g.Sum(a => a.TotalValue),
                }),
            purchaseOrders = q.AwardList.Where(a => a.PurchaseOrderId is not null)
                .GroupBy(a => new { a.PurchaseOrderId, a.PurchaseOrderNumber, a.SupplierName })
                .Select(g => new
                {
                    id = g.Key.PurchaseOrderId, number = g.Key.PurchaseOrderNumber, supplierName = g.Key.SupplierName,
                    families = g.Select(a => a.Family).OrderBy(f => f).ToList(),
                    totalValue = g.Sum(a => a.TotalValue),
                }),
            purchaseOrderId = q.PurchaseOrderId, purchaseOrderNumber = q.PurchaseOrderNumber,
            saving = q.NegotiatedValue is null ? null : new
            {
                baselineValue = q.BaselineValue, closedValue = q.NegotiatedValue,
                value = q.SavingValue, percent = q.SavingPercent,
                // as outras duas réguas (§17): a concorrência do BID e o orçamento do
                // solicitante. Nulas quando não se aplicam — fornecedor único não tem
                // concorrência, e SC sem orçamento não tem meta a bater
                competitionBaselineValue = q.CompetitionBaselineValue, competitionValue = q.CompetitionSaving,
                budgetBaselineValue = q.BudgetBaselineValue, budgetValue = q.BudgetSaving,
                notes = q.NegotiationNotes, byLabel = q.NegotiatedByLabel, at = q.NegotiatedAt,
            },
        };

        var rfq = app.MapGroup("/api/v1/quotations").RequireAuthorization();
        rfq.AddEndpointFilter(RejectSupplierRole());
        rfq.AddEndpointFilter(RequireModules(AppModules.Compras, AppModules.Aprovacao));

        // idem: a lista de processos também busca e pagina no servidor (PO-BR-012)
        rfq.MapGet("/", async (QuotationService svc, ClaimsPrincipal p, HttpContext ctx,
            string? q, string? status, int? tamanho) =>
        {
            if (!QuotationService.CanView(RoleOf(p))) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não acessa cotações.");
            QuotationStatus? situacao = Enum.TryParse<QuotationStatus>(status, true, out var st) ? st : null;
            var (itens, total) = await svc.ListAsync(q, situacao, tamanho ?? 100);
            return Ok(new { items = itens.Select(QuotationView), total, tamanho = itens.Count }, ctx);
        });

        // Central de Aprovação: processos de compra aguardando a MINHA alçada, já com preços
        rfq.MapGet("/my-approvals", async (QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var fila = await svc.PendingApprovalsAsync(RoleOf(p), ActorId(p));
            return Ok(new { items = fila.Select(QuotationView) }, ctx);
        });

        // fila de Suprimentos: PRs aprovadas aguardando cotação
        rfq.MapGet("/queue", async (QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!QuotationService.CanView(RoleOf(p))) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não acessa a fila de suprimentos.");
            var (ready, blocked) = await svc.QueueAsync();
            // itens pendentes: uma SC pode ter parte já em processo, e o que sobrou continua cotável aqui
            static object QueueRow(PurchaseRequisition r, IReadOnlyList<TrinoSupply.Foundation.Api.Procurement.QueueItem> pending,
                bool partial, string? blockReason) => new
            {
                id = r.Id, number = r.Number, requesterLabel = r.RequesterLabel, costCenter = r.CostCenter,
                justification = r.Justification, totalEstimatedValue = r.TotalEstimatedValue,
                neededBy = r.NeededBy, decidedAt = r.DecidedAt,
                assignedToId = r.AssignedToId, assignedToLabel = r.AssignedToLabel,
                blockReason, partial,
                families = pending.Select(i => i.Family).Distinct().OrderBy(f => f),
                items = pending.Select(i => new
                {
                    id = i.Id, sequence = i.Sequence, catalogCode = i.CatalogCode, description = i.Description,
                    quantity = i.Quantity, unitOfMeasure = i.UnitOfMeasure,
                    estimatedUnitPrice = i.EstimatedUnitPrice, family = i.Family,
                }),
            };
            return Ok(new
            {
                items = ready.Select(e => QueueRow(e.Pr, e.Pending, e.Partial, null))
                    .Concat(blocked.Select(b => QueueRow(b.Pr, [], false, b.Reason))),
            }, ctx);
        });

        rfq.MapGet("/{id:guid}", async (Guid id, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!QuotationService.CanView(RoleOf(p))) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não acessa cotações.");
            var q = await svc.GetAsync(id);
            return q is null ? Error(ctx, 404, "RFQ-ERR-404", "Cotação não encontrada.") : Ok(QuotationView(q), ctx);
        });

        // score multicritério da escolha (V2-P4, decisão C5): INFORMATIVO — nunca decide nem bloqueia
        rfq.MapGet("/{id:guid}/score-map", async (Guid id, QuotationService svc,
            TrinoSupply.Foundation.Api.Analytics.AnalyticsService analytics, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!QuotationService.CanView(RoleOf(p))) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não acessa cotações.");
            var q = await svc.GetAsync(id);
            if (q is null) return Error(ctx, 404, "RFQ-ERR-404", "Cotação não encontrada.");
            var latest = q.Proposals.GroupBy(pr => pr.SupplierId)
                .Select(g => g.OrderByDescending(pr => pr.VersionNumber).First()).ToList();
            var scorecard = (await analytics.ScorecardRowsAsync(12)).ToDictionary(r => r.SupplierId);
            var inputs = latest.Select(pr => new ScoreInput(
                pr.SupplierId, pr.SupplierName, pr.TotalValue, pr.DeliveryDays, pr.PaymentDays,
                scorecard.TryGetValue(pr.SupplierId, out var sc) ? sc.OtifPercent : null,
                scorecard.TryGetValue(pr.SupplierId, out var sc2) ? sc2.RiskScore : null)).ToList();
            return Ok(new
            {
                note = "Score informativo: compara as propostas mais recentes; a escolha continua sendo do comprador com justificativa.",
                items = MultiCriteriaScore.Compute(inputs),
            }, ctx);
        });

        // mapa da adjudicação por família: quem cotou cada família inteira e por quanto (V2 — compra dividida)
        rfq.MapGet("/{id:guid}/family-map", async (Guid id, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!QuotationService.CanView(RoleOf(p))) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não acessa cotações.");
            var q = await svc.GetAsync(id);
            if (q is null) return Error(ctx, 404, "RFQ-ERR-404", "Cotação não encontrada.");
            return Ok(new
            {
                note = "Cada família é um lote: só quem cotou a família inteira e está homologado " +
                       "pode levá-la. O valor inclui o rateio proporcional de frete, impostos e " +
                       "desconto da proposta.",
                items = (await svc.FamilyMapAsync(q)).Select(l => new
                {
                    family = l.Family, itemCount = l.ItemCount, quantity = l.Quantity,
                    offers = l.Offers.Select(o => new
                    {
                        supplierId = o.SupplierId, supplierName = o.SupplierName,
                        proposalId = o.ProposalId, proposalVersion = o.ProposalVersion,
                        itemsValue = o.ItemsValue, totalValue = o.TotalValue,
                        deliveryDays = o.DeliveryDays, paymentTerms = o.PaymentTerms,
                        complete = o.Complete, cheapest = o.Cheapest,
                        // situação do fornecedor no cadastro: é o que deixa a tela antecipar
                        // SUP-ERR-030 e RFQ-ERR-040 em vez de recusar depois da justificativa
                        homologation = o.Homologation, active = o.Active, canWin = o.CanWin,
                    }),
                }),
            }, ctx);
        });

        rfq.MapGet("/{id:guid}/timeline", async (Guid id, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!QuotationService.CanView(RoleOf(p))) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não acessa cotações.");
            var events = await svc.TimelineAsync(id);
            return Ok(new
            {
                items = events.Select(e => new
                {
                    eventType = e.EventType, description = e.Description,
                    fromStatus = e.FromStatus is null ? null : QStatusLabel(e.FromStatus.Value),
                    toStatus = e.ToStatus is null ? null : QStatusLabel(e.ToStatus.Value),
                    actorLabel = e.ActorLabel, note = e.Note, documentId = e.DocumentId, occurredAt = e.OccurredAt,
                }),
            }, ctx);
        });

        rfq.MapPost("/", async (CreateQuotationRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!QuotationService.CanConduct(role)) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não abre cotações.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var kind = body.Kind?.ToUpperInvariant() switch
            {
                "BID" => QuotationKind.Bid, "SERVICO" => QuotationKind.Service, _ => QuotationKind.Purchase,
            };
            // agrupamento multi-SC (V2): itens de várias SCs do mesmo centro; sem itens, vale o fluxo 1:1 por PrId
            var (q, error) = body.PrItemIds is { Count: > 0 }
                ? await svc.CreateFromItemsAsync(actor, body.PrItemIds, kind, body.Deadline, body.Notes)
                : body.PrId is { } prId
                    ? await svc.CreateFromPrAsync(actor, prId, kind, body.Deadline, body.Notes)
                    : (null, new UserError("RFQ-ERR-060", "Informe a solicitação (prId) ou os itens (prItemIds) para abrir o processo."));
            return error is not null ? Error(ctx, 422, error.Code, error.Message)
                : Results.Json(new { data = QuotationView(q!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        rfq.MapPost("/{id:guid}/suppliers", async (Guid id, InviteSuppliersRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!QuotationService.CanConduct(role)) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não convida fornecedores.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var (q, error) = await svc.InviteSuppliersAsync(actor, id, body.SupplierIds ?? []);
            return error is not null ? Error(ctx, error.Code == "RFQ-ERR-010" ? 400 : 409, error.Code, error.Message) : Ok(QuotationView(q!), ctx);
        });

        // registro interno de proposta recebida fora do portal (e-mail/telefone)
        rfq.MapPost("/{id:guid}/proposals", async (Guid id, InternalProposalRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!QuotationService.CanConduct(role)) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não registra propostas.");
            var input = new ProposalInput(body.DeliveryDays, body.PaymentTerms, body.FreightValue, body.ValidUntil, body.Notes,
                (body.Items ?? []).Select(i => new ProposalItemInput(i.QuotationItemId, i.UnitPrice, i.Quantity)).ToList(),
                body.DiscountValue, body.Currency, body.PaymentDays, body.TaxValue, body.OtherCosts,
                body.PaymentMethodName);
            var (proposal, error) = await svc.SubmitProposalAsync(id, body.SupplierId, input, "INTERNO", p.FindFirstValue("name") ?? "Usuário");
            if (error is not null) return Error(ctx, error.Code == "RFQ-ERR-020" ? 409 : 400, error.Code, error.Message);
            var q = await svc.GetAsync(id);
            return Ok(QuotationView(q!), ctx);
        });

        rfq.MapPost("/{id:guid}/close", async (Guid id, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!QuotationService.CanConduct(role)) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não encerra cotações.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var (q, error) = await svc.CloseForAnalysisAsync(actor, id);
            return error is not null ? Error(ctx, error.Code == "RFQ-ERR-021" ? 422 : 409, error.Code, error.Message) : Ok(QuotationView(q!), ctx);
        });

        rfq.MapPost("/{id:guid}/select-winner", async (Guid id, SelectWinnerRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!QuotationService.CanConduct(role)) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não seleciona fornecedores.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var criteria = body.Criteria is { Count: > 0 } ? string.Join(", ", body.Criteria) : null;
            // compra dividida: uma escolha por família; sem 'awards', o vencedor leva todas as famílias
            var (q, error) = body.Awards is { Count: > 0 }
                ? await svc.AwardByFamilyAsync(actor, id, body.Awards.Select(a => new AwardInput(
                        a.Family, a.ProposalId,
                        a.Criteria is { Count: > 0 } ? string.Join(", ", a.Criteria) : criteria,
                        string.IsNullOrWhiteSpace(a.Justification) ? body.Justification : a.Justification)).ToList())
                : await svc.SelectWinnerAsync(actor, id, body.ProposalId, criteria, body.Justification);
            return error is not null ? Error(ctx, error.Code == "RFQ-ERR-020" ? 409 : 422, error.Code, error.Message) : Ok(QuotationView(q!), ctx);
        });

        rfq.MapPost("/{id:guid}/manager-decision", async (Guid id, QuotationDecisionRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!QuotationService.CanApproveAsManager(role))
                return Error(ctx, 403, "RFQ-ERR-900", "A aprovação gerencial cabe ao gestor de suprimentos ou administrador.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var (q, error) = await svc.ManagerDecisionAsync(actor, id, body.Decision, body.Reason);
            return error is not null
                ? Error(ctx, error.Code switch { "RFQ-ERR-020" => 409, "RFQ-ERR-030" => 422, _ => 400 }, error.Code, error.Message)
                : Ok(QuotationView(q!), ctx);
        });

        rfq.MapPost("/{id:guid}/director-decision", async (Guid id, QuotationDecisionRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!QuotationService.CanApproveAsDirector(role))
                return Error(ctx, 403, "RFQ-ERR-900", "A aprovação da diretoria cabe ao diretor ou administrador.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var (q, error) = await svc.DirectorDecisionAsync(actor, id, body.Decision, body.Reason);
            return error is not null
                ? Error(ctx, error.Code switch { "RFQ-ERR-020" => 409, "RFQ-ERR-030" => 422, _ => 400 }, error.Code, error.Message)
                : Ok(QuotationView(q!), ctx);
        });

        // a OC é fechada no SENIOR: aqui o comprador registra o número dela e o processo segue
        rfq.MapPost("/{id:guid}/register-po", async (Guid id, RegisterPoRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!QuotationService.CanConduct(role)) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não registra ordens de compra.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var (order, error) = await svc.RegisterErpPurchaseOrderAsync(actor, id, body.ErpNumber, body.IssuedOn, body.Notes,
                body.OverLimitJustification, body.SupplierId, body.NoErpReason);
            return error is not null
                ? Error(ctx, error.Code is "RFQ-ERR-041" or "CT-ERR-010" or "RFQ-ERR-042" or "RFQ-ERR-043" ? 422 : 409, error.Code, error.Message)
                : Results.Json(new { data = PoView(order!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        // ganho de negociação: valor fechado (ou desconto em %) vira nova versão da proposta + saving
        rfq.MapPost("/{id:guid}/negotiation", async (Guid id, NegotiationRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!QuotationService.CanConduct(role)) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não negocia propostas.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var (_, error) = await svc.RegisterNegotiationAsync(actor, id, body.SupplierId, body.ClosedValue, body.DiscountPercent, body.Notes);
            if (error is not null) return Error(ctx, error.Code == "RFQ-ERR-020" ? 409 : 422, error.Code, error.Message);
            var q = await svc.GetAsync(id);
            return Ok(QuotationView(q!), ctx);
        });

        rfq.MapPost("/{id:guid}/cancel", async (Guid id, ReasonRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!QuotationService.CanConduct(role)) return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não cancela cotações.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var (q, error) = await svc.CancelAsync(actor, id, body.Reason);
            return error is not null ? Error(ctx, error.Code == "RFQ-ERR-020" ? 409 : 400, error.Code, error.Message) : Ok(QuotationView(q!), ctx);
        });

        // ==== Portal do Fornecedor (RFQ-001 §5) ======================================
        var portal = app.MapGroup("/api/v1/portal");

        portal.MapPost("/login", async (PortalLoginRequest body, SupplierService svc, TokenService tokens, TimeProvider clock, HttpContext ctx) =>
        {
            var supplier = await svc.PortalLoginAsync(body.TaxId, body.AccessKey);
            if (supplier is null)
                return Error(ctx, 401, "RFQ-ERR-051", "CNPJ/CPF ou chave de acesso inválidos, ou fornecedor sem acesso ao portal.");
            var token = tokens.CreateSupplierToken(supplier.Id, supplier.TradeName ?? supplier.LegalName, clock.GetUtcNow());
            return Ok(new
            {
                accessToken = token, tokenType = "Bearer", expiresIn = 3600,
                supplier = new { id = supplier.Id, name = supplier.TradeName ?? supplier.LegalName, taxId = supplier.TaxId },
            }, ctx);
        }).RequireRateLimiting("auth");

        // o fornecedor só enxerga as próprias cotações — nunca dados de outros fornecedores
        static object PortalQuotationView(Quotation q, Guid supplierId) => new
        {
            id = q.Id, number = q.Number, kind = QKindLabel(q.Kind),
            open = q.Status == QuotationStatus.Open,
            status = q.Status == QuotationStatus.Open ? "ABERTA" : "ENCERRADA",
            deadline = q.Deadline, notes = q.Notes, createdAt = q.CreatedAt,
            items = q.Items.OrderBy(i => i.Sequence).Select(i => new
            {
                id = i.Id, sequence = i.Sequence, description = i.Description,
                quantity = i.Quantity, unitOfMeasure = i.UnitOfMeasure,
            }),
            myProposals = q.Proposals.Where(p => p.SupplierId == supplierId)
                .OrderByDescending(p => p.VersionNumber)
                .Select(p => new
                {
                    id = p.Id, version = p.VersionNumber, totalValue = p.TotalValue,
                    deliveryDays = p.DeliveryDays, paymentTerms = p.PaymentTerms,
                    paymentMethodName = p.PaymentMethodName, freightValue = p.FreightValue,
                    validUntil = p.ValidUntil, notes = p.Notes, submittedAt = p.SubmittedAt,
                    attachmentDocumentId = p.AttachmentDocumentId, attachmentFileName = p.AttachmentFileName,
                    items = p.Items.Select(i => new { quotationItemId = i.QuotationItemId, unitPrice = i.UnitPrice, quantity = i.Quantity }),
                }),
        };

        portal.MapGet("/quotations", async (AppDbContext db, ClaimsPrincipal p, HttpContext ctx, string? number) =>
        {
            if (PortalSupplierId(p) is not { } sid) return Error(ctx, 403, "RFQ-ERR-050", "Acesso exclusivo do Portal do Fornecedor.");
            var ids = await db.QuotationSuppliers.Where(s => s.SupplierId == sid).Select(s => s.QuotationId).ToListAsync();
            var query = db.Quotations.Include(q => q.Items).Include(q => q.Proposals).ThenInclude(x => x.Items)
                .Where(q => ids.Contains(q.Id));
            if (!string.IsNullOrWhiteSpace(number)) query = query.Where(q => q.Number.Contains(number.Trim().ToUpperInvariant()));
            var list = await query.OrderByDescending(q => q.CreatedAt).Take(100).ToListAsync();
            return Ok(new { items = list.Select(q => PortalQuotationView(q, sid)) }, ctx);
        }).RequireAuthorization();

        portal.MapGet("/quotations/{id:guid}", async (Guid id, AppDbContext db, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (PortalSupplierId(p) is not { } sid) return Error(ctx, 403, "RFQ-ERR-050", "Acesso exclusivo do Portal do Fornecedor.");
            if (!await db.QuotationSuppliers.AnyAsync(s => s.QuotationId == id && s.SupplierId == sid))
                return Error(ctx, 404, "RFQ-ERR-404", "Cotação não encontrada."); // isolamento: 404, nunca vaza existência
            var q = await db.Quotations.Include(x => x.Items).Include(x => x.Proposals).ThenInclude(x => x.Items)
                .SingleAsync(x => x.Id == id);
            return Ok(PortalQuotationView(q, sid), ctx);
        }).RequireAuthorization();

        portal.MapPost("/quotations/{id:guid}/proposal", async (Guid id, PortalProposalRequest body, QuotationService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (PortalSupplierId(p) is not { } sid) return Error(ctx, 403, "RFQ-ERR-050", "Acesso exclusivo do Portal do Fornecedor.");
            var input = new ProposalInput(body.DeliveryDays, body.PaymentTerms, body.FreightValue, body.ValidUntil, body.Notes,
                (body.Items ?? []).Select(i => new ProposalItemInput(i.QuotationItemId, i.UnitPrice, i.Quantity)).ToList(),
                TaxValue: body.TaxValue, OtherCosts: body.OtherCosts);
            var (proposal, error) = await svc.SubmitProposalAsync(id, sid, input, "PORTAL", p.FindFirstValue("name") ?? "Fornecedor");
            return error is not null
                ? Error(ctx, error.Code switch { "RFQ-ERR-050" => 403, "RFQ-ERR-020" => 409, _ => 400 }, error.Code, error.Message)
                : Results.Json(new
                {
                    data = new { id = proposal!.Id, version = proposal.VersionNumber, totalValue = proposal.TotalValue },
                    correlationId = CorrelationId(ctx),
                }, statusCode: 201);
        }).RequireAuthorization();

        portal.MapPost("/proposals/{proposalId:guid}/attachment", async (Guid proposalId, HttpRequest request, AppDbContext db, TimeProvider clock, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (PortalSupplierId(p) is not { } sid) return Error(ctx, 403, "RFQ-ERR-050", "Acesso exclusivo do Portal do Fornecedor.");
            var proposal = await db.Proposals.SingleOrDefaultAsync(x => x.Id == proposalId && x.SupplierId == sid);
            if (proposal is null) return Error(ctx, 404, "RFQ-ERR-404", "Proposta não encontrada.");
            if (!request.HasFormContentType) return Error(ctx, 400, "DOC-ERR-001", "Envie o arquivo como multipart/form-data.");
            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0) return Error(ctx, 400, "DOC-ERR-001", "Nenhum arquivo enviado.");
            if (file.Length > StoredDocument.MaxSizeBytes) return Error(ctx, 400, "DOC-ERR-002", "Arquivo acima de 10 MB.");
            if (!StoredDocument.AllowedContentTypes.Contains(file.ContentType))
                return Error(ctx, 400, "DOC-ERR-003", "Formato não permitido: envie PDF, imagem (PNG/JPG) ou Office (XLSX/DOCX).");
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var doc = new StoredDocument
            {
                FileName = Path.GetFileName(file.FileName), ContentType = file.ContentType, SizeBytes = file.Length,
                Content = ms.ToArray(), EntityType = "PROPOSAL", EntityId = proposal.Id, SupplierId = sid,
                UploadedByLabel = p.FindFirstValue("name") ?? "Fornecedor", UploadedAt = clock.GetUtcNow(),
            };
            db.StoredDocuments.Add(doc);
            proposal.AttachmentDocumentId = doc.Id;
            proposal.AttachmentFileName = doc.FileName;
            await db.SaveChangesAsync();
            return Ok(new { documentId = doc.Id, fileName = doc.FileName }, ctx);
        }).RequireAuthorization()
            .RequireRateLimiting("upload").ComTetoDeUpload(StoredDocument.MaxRequestBytes);

        // anexo da proposta registrada internamente: o PDF/planilha que o fornecedor enviou por fora
        app.MapPost("/api/v1/quotations/{id:guid}/proposals/{proposalId:guid}/attachment",
            async (Guid id, Guid proposalId, HttpRequest request, AppDbContext db, QuotationService svc,
                   TimeProvider clock, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!QuotationService.CanConduct(RoleOf(p)))
                return Error(ctx, 403, "RFQ-ERR-900", "Seu papel não conduz o processo de cotação.");
            var proposal = await db.Proposals.SingleOrDefaultAsync(x => x.Id == proposalId && x.QuotationId == id);
            if (proposal is null) return Error(ctx, 404, "RFQ-ERR-404", "Proposta não encontrada neste processo.");
            if (!request.HasFormContentType) return Error(ctx, 400, "DOC-ERR-001", "Envie o arquivo como multipart/form-data.");
            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0) return Error(ctx, 400, "DOC-ERR-001", "Nenhum arquivo enviado.");
            if (file.Length > StoredDocument.MaxSizeBytes) return Error(ctx, 400, "DOC-ERR-002", "Arquivo acima de 10 MB.");
            if (!StoredDocument.AllowedContentTypes.Contains(file.ContentType))
                return Error(ctx, 400, "DOC-ERR-003", "Formato não permitido: envie PDF, planilha (XLSX/XLS/CSV), imagem ou DOCX.");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var doc = new StoredDocument
            {
                FileName = Path.GetFileName(file.FileName), ContentType = file.ContentType, SizeBytes = file.Length,
                Content = ms.ToArray(), EntityType = "PROPOSAL", EntityId = proposal.Id, SupplierId = proposal.SupplierId,
                UploadedByLabel = p.FindFirstValue("name") ?? "Suprimentos", UploadedAt = clock.GetUtcNow(),
            };
            db.StoredDocuments.Add(doc);
            proposal.AttachmentDocumentId = doc.Id;
            proposal.AttachmentFileName = doc.FileName;

            // registra no histórico do processo: a cotação recebida ficou arquivada
            var q = await svc.GetAsync(id);
            if (q is not null)
                svc.RecordAttachmentEvent(q, new Actor(ActorId(p), p.FindFirstValue("name") ?? "Suprimentos", RoleOf(p)),
                    proposal.SupplierName, doc.FileName);
            await db.SaveChangesAsync();
            return Ok(new { documentId = doc.Id, fileName = doc.FileName }, ctx);
        }).RequireAuthorization().AddEndpointFilter(RejectSupplierRole())
            .RequireRateLimiting("upload").ComTetoDeUpload(StoredDocument.MaxRequestBytes);
    }
}
