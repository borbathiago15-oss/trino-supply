using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Inventory;
using TrinoSupply.Foundation.Api.Materials;
using TrinoSupply.Foundation.Api.Procurement;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// MMS-004 e MMS-005 — o estoque: saldo, entrada por recebimento, saída e
/// movimentações. E MMS-003 — a solicitação de material, que é como o
/// almoxarifado recebe o pedido e o atende.
///
/// Os dois vieram juntos porque são o mesmo balcão visto dos dois lados: o que
/// sai do estoque é o que a solicitação de material pediu.
/// </summary>
public static class EstoqueRotas
{
    public static void MapEstoque(this WebApplication app)
    {
        // ---- MMS-004 — Estoque (MVP) + MMS-005 — entrada por recebimento -------------
        static object MovementView(StockMovement m) => new
        {
            id = m.Id, number = m.Number,
            type = m.Type == MovementType.Entry ? "ENTRADA" : "SAIDA",
            origin = m.Origin switch
            {
                MovementOrigin.Receiving => "RECEBIMENTO",
                MovementOrigin.Return => "DEVOLUCAO",
                MovementOrigin.InitialLoad => "CARGA_INICIAL",
                MovementOrigin.Fulfillment => "ATENDIMENTO",
                _ => "CONSUMO",
            },
            originReference = m.OriginReference,
            itemCode = m.ItemCode, itemDescription = m.ItemDescription, unitOfMeasure = m.UnitOfMeasure,
            locationId = m.LocationId, quantity = m.Quantity,
            balanceBefore = m.BalanceBefore, balanceAfter = m.BalanceAfter,
            performedByLabel = m.PerformedByLabel, performedAt = m.PerformedAt,
            materialRequisitionId = m.MaterialRequisitionId,
        };

        var inv = app.MapGroup("/api/v1/inventory").RequireAuthorization();
        inv.AddEndpointFilter(RequireModules(AppModules.Estoque, AppModules.Compras));

        inv.MapGet("/locations", async (InventoryService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!CanViewStock(p)) return Error(ctx, 403, "IV-ERR-900", "Seu usuário não acessa o estoque.");
            return Ok(new { items = (await svc.LocationsAsync()).Select(l => new { id = l.Id, code = l.Code, name = l.Name }) }, ctx);
        });

        inv.MapPost("/locations", async (CreateLocationRequest body, InventoryService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (role is not (Roles.WarehouseSupervisor or Roles.SupplyManager or Roles.SystemAdministrator))
                return Error(ctx, 403, "IV-ERR-900", "Somente supervisor, gestor ou administrador gerenciam locais.");
            var (location, error) = await svc.CreateLocationAsync(ActorId(p), body.Code, body.Name);
            return error is not null ? Error(ctx, 400, error.Code, error.Message)
                : Results.Json(new { data = new { id = location!.Id, code = location.Code, name = location.Name }, correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        inv.MapGet("/balances", async (InventoryService svc, ClaimsPrincipal p, HttpContext ctx, Guid? itemId, Guid? locationId) =>
        {
            if (!CanViewStock(p)) return Error(ctx, 403, "IV-ERR-900", "Seu usuário não acessa o estoque.");
            var rows = await svc.BalancesAsync(itemId, locationId);
            return Ok(new
            {
                items = rows.Select(r => new
                {
                    itemId = r.item.Id, itemCode = r.item.Code, itemDescription = r.item.Description,
                    family = r.item.Family, unitOfMeasure = r.item.UnitOfMeasure,
                    locationId = r.location.Id, locationCode = r.location.Code,
                    totalQty = r.balance.TotalQty, reservedQty = r.balance.ReservedQty, availableQty = r.balance.AvailableQty,
                    lastMovementAt = r.balance.LastMovementAt,
                }),
            }, ctx);
        });

        inv.MapGet("/movements", async (InventoryService svc, ClaimsPrincipal p, HttpContext ctx, Guid? itemId, Guid? locationId) =>
        {
            if (!CanViewStock(p)) return Error(ctx, 403, "IV-ERR-900", "Seu usuário não acessa o estoque.");
            return Ok(new { items = (await svc.MovementsAsync(itemId, locationId)).Select(MovementView) }, ctx);
        });

        // Entrada de material (MMS-005 MVP: recebimento conferido / devolução / carga inicial)
        inv.MapPost("/entries", async (EntryRequest body, InventoryService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!CanOperateStock(p)) return Error(ctx, 403, "IV-ERR-900", "Seu usuário não registra entradas.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var origin = body.Origin?.ToUpperInvariant() switch
            {
                "DEVOLUCAO" => MovementOrigin.Return,
                "CARGA_INICIAL" => MovementOrigin.InitialLoad,
                _ => MovementOrigin.Receiving,
            };
            var (movement, error) = await svc.RegisterEntryAsync(actor, body.CatalogItemId, body.LocationId, body.Quantity, origin, body.OriginReference);
            return error is not null ? Error(ctx, error.Code == "IV-ERR-020" ? 422 : 400, error.Code, error.Message)
                : Results.Json(new { data = MovementView(movement!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        // Saída manual de material (consumo) — nunca deixa o saldo negativo (MMS-RG-04)
        inv.MapPost("/issues", async (IssueRequest body, InventoryService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!CanOperateStock(p)) return Error(ctx, 403, "IV-ERR-900", "Seu usuário não registra saídas.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var (movement, error) = await svc.RegisterIssueAsync(actor, body.CatalogItemId, body.LocationId, body.Quantity, MovementOrigin.Consumption, body.OriginReference);
            return error is not null ? Error(ctx, error.Code == "IV-ERR-020" ? 422 : 400, error.Code, error.Message)
                : Results.Json(new { data = MovementView(movement!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        // ---- MMS-003 — Solicitação de Material (MVP) ---------------------------------
        static object MrView(MaterialRequisition r) => new
        {
            id = r.Id, number = r.Number,
            status = r.Status switch
            {
                MaterialRequisitionStatus.Submitted => "AGUARDANDO_APROVACAO",
                MaterialRequisitionStatus.Approved => "AGUARDANDO_ALMOXARIFADO",
                MaterialRequisitionStatus.Fulfilled => "ATENDIDA",
                MaterialRequisitionStatus.PartiallyFulfilled => "ATENDIDA_PARCIAL",
                MaterialRequisitionStatus.PurchaseRoute => "ROTA_DE_COMPRA",
                MaterialRequisitionStatus.Rejected => "RECUSADA",
                _ => "CANCELADA",
            },
            costCenter = r.CostCenter, notes = r.Notes,
            requesterId = r.RequesterId, requesterLabel = r.RequesterLabel,
            fulfilledByLabel = r.FulfilledByLabel, fulfilledAt = r.FulfilledAt, cancelReason = r.CancelReason,
            approvedByLabel = r.ApprovedByLabel, approvedAt = r.ApprovedAt, decisionReason = r.DecisionReason,
            purchaseRequisitionId = r.PurchaseRequisitionId, purchaseRequisitionNumber = r.PurchaseRequisitionNumber,
            assignedToId = r.AssignedToId, assignedToLabel = r.AssignedToLabel,
            assignedByLabel = r.AssignedByLabel, assignedAt = r.AssignedAt,
            items = r.Items.Select(i => new
            {
                itemId = i.Id, catalogCode = i.CatalogCode, description = i.Description,
                unitOfMeasure = i.UnitOfMeasure, quantity = i.Quantity,
                approvedQuantity = i.ApprovedQuantity, effectiveQuantity = i.EffectiveQuantity,
                fulfilledQuantity = i.FulfilledQuantity,
                pendingQuantity = i.EffectiveQuantity - i.FulfilledQuantity,
                status = i.Status switch
                {
                    MaterialItemStatus.Fulfilled => "ENTREGUE",
                    MaterialItemStatus.PartiallyFulfilled => "ENTREGUE_PARCIAL",
                    MaterialItemStatus.PurchaseRoute => "ROTA_DE_COMPRA",
                    _ => "PENDENTE",
                },
            }),
            createdAt = r.CreatedAt,
        };

        // Painel de atendimentos: substitui o dashboard de estoque (a posição de saldo fica no
        // sistema de almoxarifado da operação — revisão do módulo, 2026-08-26)
        app.MapGet("/api/v1/material-requisitions/panel", async (AppDbContext db, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!CanOperateStock(p) && !MaterialRequisitionService.CanSeeAll(RoleOf(p)))
                return Error(ctx, 403, "MR-ERR-001", "Seu usuário não acessa o painel de atendimentos.");

            var mrsAll = await db.MaterialRequisitions.Include(r => r.Items)
                .OrderByDescending(r => r.CreatedAt).Take(500).ToListAsync();

            object Bloco(IEnumerable<MaterialRequisition> fonte) => fonte.Select(r => new
            {
                id = r.Id, number = r.Number, costCenter = r.CostCenter, requesterLabel = r.RequesterLabel,
                createdAt = r.CreatedAt, approvedAt = r.ApprovedAt, fulfilledAt = r.FulfilledAt,
                fulfilledByLabel = r.FulfilledByLabel,
                purchaseRequisitionNumber = r.PurchaseRequisitionNumber,
                items = r.Items.Count,
                pending = r.Items.Sum(i => i.EffectiveQuantity - i.FulfilledQuantity),
                summary = string.Join(" · ", r.Items.Take(3).Select(i => $"{i.EffectiveQuantity:0.##}× {i.Description}")),
            }).ToList();

            var aguardando = mrsAll.Where(r => r.Status == MaterialRequisitionStatus.Submitted).ToList();
            var andamento = mrsAll.Where(r => r.Status == MaterialRequisitionStatus.Approved).ToList();
            var concluidos = mrsAll.Where(r => r.Status == MaterialRequisitionStatus.Fulfilled).ToList();
            var parciais = mrsAll.Where(r => r.Status is MaterialRequisitionStatus.PartiallyFulfilled
                                             or MaterialRequisitionStatus.PurchaseRoute).ToList();

            return Ok(new
            {
                totals = new
                {
                    aguardandoAprovacao = aguardando.Count, emAndamento = andamento.Count,
                    concluidos = concluidos.Count, parciais = parciais.Count,
                },
                aguardandoAprovacao = Bloco(aguardando),
                emAndamento = Bloco(andamento),
                concluidos = Bloco(concluidos.Take(50)),
                parciais = Bloco(parciais),
                porCentro = mrsAll.GroupBy(r => r.CostCenter).Select(g => new
                {
                    costCenter = g.Key, total = g.Count(),
                    emAndamento = g.Count(r => r.Status == MaterialRequisitionStatus.Approved),
                    concluidos = g.Count(r => r.Status == MaterialRequisitionStatus.Fulfilled),
                    parciais = g.Count(r => r.Status is MaterialRequisitionStatus.PartiallyFulfilled
                                            or MaterialRequisitionStatus.PurchaseRoute),
                }).OrderByDescending(x => x.total).ToList(),
                porSolicitante = mrsAll.GroupBy(r => r.RequesterLabel).Select(g => new
                {
                    requesterLabel = g.Key, total = g.Count(),
                    emAndamento = g.Count(r => r.Status == MaterialRequisitionStatus.Approved),
                    concluidos = g.Count(r => r.Status == MaterialRequisitionStatus.Fulfilled),
                    parciais = g.Count(r => r.Status is MaterialRequisitionStatus.PartiallyFulfilled
                                            or MaterialRequisitionStatus.PurchaseRoute),
                }).OrderByDescending(x => x.total).ToList(),
            }, ctx);
        }).RequireAuthorization().AddEndpointFilter(RequireModules(AppModules.Material, AppModules.Estoque));

        var mrs = app.MapGroup("/api/v1/material-requisitions").RequireAuthorization();
        mrs.AddEndpointFilter(RequireModules(AppModules.Material, AppModules.Estoque));

        mrs.MapGet("/", async (MaterialRequisitionService svc, ClaimsPrincipal p, HttpContext ctx, bool? queue, bool? mine) =>
        {
            var role = RoleOf(p);
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            if (queue == true && !CanOperateStock(p))
                return Error(ctx, 403, "MR-ERR-001", "Seu papel não acessa a fila do almoxarifado.");
            if (queue != true && !(MaterialRequisitionService.CanRequest(role) || MaterialRequisitionService.CanSeeAll(role)))
                return Error(ctx, 403, "MR-ERR-001", "Seu papel não acessa solicitações de material.");
            var list = await svc.ListAsync(actor, queue == true);
            if (mine == true) list = list.Where(r => r.AssignedToId == actor.Id).ToList();
            return Ok(new { items = list.Select(MrView) }, ctx);
        });

        mrs.MapPost("/", async (CreateMaterialRequisitionRequest body, MaterialRequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!MaterialRequisitionService.CanRequest(role))
                return Error(ctx, 403, "MR-ERR-001", "Seu papel não cria solicitações de material.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var items = (body.Items ?? []).Select(i => new MaterialItemInput(i.CatalogItemId, i.Quantity)).ToList();
            var (mr, error) = await svc.CreateAsync(actor, body.CostCenter, body.Notes, items);
            return error is not null ? Error(ctx, 400, error.Code, error.Message)
                : Results.Json(new { data = MrView(mr!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        // aprovação do responsável do centro (Nível 1), podendo ajustar a quantidade liberada
        mrs.MapPost("/{id:guid}/approve", async (Guid id, ApproveMaterialRequest body, MaterialRequisitionService svc,
            ClaimsPrincipal p, HttpContext ctx) =>
        {
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", RoleOf(p));
            var lines = (body.Items ?? []).Select(i => new MaterialRequisitionService.ApprovalLine(i.ItemId, i.Quantity)).ToList();
            var (mr, error) = await svc.ApproveAsync(actor, id, lines, body.Notes);
            return error is not null
                ? Error(ctx, error.Code switch { "MR-ERR-404" => 404, "MR-ERR-040" => 409, "MR-ERR-002" => 403, _ => 422 },
                        error.Code, error.Message)
                : Ok(MrView(mr!), ctx);
        });

        mrs.MapPost("/{id:guid}/reject", async (Guid id, ReasonRequest body, MaterialRequisitionService svc,
            ClaimsPrincipal p, HttpContext ctx) =>
        {
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", RoleOf(p));
            var (mr, error) = await svc.RejectAsync(actor, id, body.Reason);
            return error is not null
                ? Error(ctx, error.Code switch { "MR-ERR-404" => 404, "MR-ERR-040" => 409, "MR-ERR-002" => 403, _ => 422 },
                        error.Code, error.Message)
                : Ok(MrView(mr!), ctx);
        });

        // atendimento do estoque: quantidade entregue por item; o que faltar vira solicitação de compra
        mrs.MapPost("/{id:guid}/fulfill", async (Guid id, FulfillRequest body, MaterialRequisitionService svc,
            RequisitionService purchases, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var role = RoleOf(p);
            if (!CanOperateStock(p))
                return Error(ctx, 403, "MR-ERR-001", "Seu usuário não atende solicitações de material.");
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", role);
            var lines = (body.Items ?? []).Select(i => new MaterialRequisitionService.FulfillLine(i.ItemId, i.Quantity)).ToList();
            var (mr, error) = await svc.FulfillAsync(actor, id, lines, purchases);
            return error is not null
                ? Error(ctx, error.Code switch { "MR-ERR-404" => 404, "MR-ERR-040" => 409, _ => 422 }, error.Code, error.Message)
                : Ok(MrView(mr!), ctx);
        });

        mrs.MapPost("/{id:guid}/cancel", async (Guid id, ReasonRequest body, MaterialRequisitionService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            var actor = new Actor(ActorId(p), p.FindFirstValue("name") ?? "Usuário", RoleOf(p));
            var (mr, error) = await svc.CancelAsync(actor, id, body.Reason);
            return error is not null
                ? Error(ctx, error.Code switch { "MR-ERR-404" => 404, "MR-ERR-040" => 409, "MR-ERR-001" => 403, _ => 400 }, error.Code, error.Message)
                : Ok(MrView(mr!), ctx);
        });
    }
}
