using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Insights;
using TrinoSupply.Foundation.Api.Materials;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;
using static TrinoSupply.Foundation.Api.Rotas.Anexos;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// A central de avisos do painel: o que está parado e com quem, montado por
/// papel. É o que alimenta a trilha do processo e os contadores do menu.
///
/// Saiu do Program.cs no ARQ-A. O conteúdo é o mesmo — grupo, filtros e
/// handlers na mesma ordem —, só mudou de arquivo.
/// </summary>
public static class AvisoRotas
{
    /// <summary>O nome do achado em português, para o aviso não falar por código.</summary>
    private static string RotuloDoAchado(string codigo) => codigo switch
    {
        "INS-01" => "sobrepreço",
        "INS-02" => "possível fracionamento",
        "INS-04" => "concentração de fornecedor",
        "INS-05" => "compra sem O.C. do ERP",
        "INS-06" => "atraso recorrente de fornecedor",
        _ => "risco em compras",
    };

    public static void MapAvisos(this WebApplication app)
    {
        // ---- Dashboard — central de avisos (atrasos, aprovações, fila, demandas) -----
        app.MapGet("/api/v1/dashboard", async (AppDbContext db, RequisitionService prSvc,
            InsightsService insights, ClaimsPrincipal p, HttpContext ctx, TimeProvider clock) =>
        {
            var role = RoleOf(p);
            var uid = ActorId(p);
            var mods = ModulesOf(p);
            var actor = new Actor(uid, p.FindFirstValue("name") ?? "Usuário", role);
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var alerts = new List<object>();

            // Requisições com data de necessidade vencida e ainda não concluídas (escopo do papel)
            if (mods.Contains(AppModules.Solicitacoes) || mods.Contains(AppModules.Aprovacao))
            {
                var overdueQ = db.Requisitions.Where(r =>
                    r.NeededBy != null && r.NeededBy < today &&
                    (r.Status == RequisitionStatus.Submitted || r.Status == RequisitionStatus.InApproval
                     || r.Status == RequisitionStatus.Approved));
                if (!actor.SeesAll) overdueQ = overdueQ.Where(r => r.RequesterId == uid);
                var overdue = await overdueQ.CountAsync();
                if (overdue > 0) alerts.Add(new
                {
                    kind = "ATRASO", severity = "alta", count = overdue, view = "pr-mine",
                    text = $"{overdue} pedido(s) com data de necessidade vencida e ainda não concluído(s).",
                });
            }

            // Fila de aprovação: solicitações legadas em aprovação (fluxo antigo, sem preço)
            if (mods.Contains(AppModules.Aprovacao) && actor.CanDecide)
            {
                var pending = (await prSvc.PendingApprovalsAsync(actor)).Count;
                if (pending > 0) alerts.Add(new
                {
                    kind = "APROVACAO", severity = "media", count = pending, view = "pr-approvals",
                    text = $"{pending} solicitação(ões) do fluxo anterior aguardando a sua autorização.",
                });
            }

            // Meus pedidos aguardando aprovação / devolvidos para ajuste
            if (mods.Contains(AppModules.Solicitacoes) && actor.CanCreate)
            {
                var waiting = await db.Requisitions.CountAsync(r => r.RequesterId == uid
                    && (r.Status == RequisitionStatus.Submitted || r.Status == RequisitionStatus.InApproval));
                if (waiting > 0) alerts.Add(new
                {
                    kind = "AGUARDANDO", severity = "info", count = waiting, view = "pr-mine",
                    text = $"{waiting} pedido(s) seu(s) em andamento com Suprimentos.",
                });
                var returned = await db.Requisitions.CountAsync(r => r.RequesterId == uid && r.Status == RequisitionStatus.Returned);
                if (returned > 0) alerts.Add(new
                {
                    kind = "DEVOLVIDO", severity = "alta", count = returned, view = "pr-mine",
                    text = $"{returned} pedido(s) devolvido(s) para ajuste — revise e reenvie.",
                });
            }

            // Fila do almoxarifado
            if (mods.Contains(AppModules.Estoque) && CanOperateStock(p))
            {
                var queue = await db.MaterialRequisitions.CountAsync(r => r.Status == MaterialRequisitionStatus.Submitted);
                if (queue > 0) alerts.Add(new
                {
                    kind = "ALMOXARIFADO", severity = "media", count = queue, view = "wh-queue",
                    text = $"{queue} solicitação(ões) de material aguardando atendimento.",
                });
            }

            // Minhas demandas (tickets designados na triagem)
            {
                var linkedPos = db.PurchaseOrders.Where(o => o.SourcePrId != null && o.Status != PurchaseOrderStatus.Cancelled).Select(o => o.SourcePrId!.Value);
                var mine = await db.Requisitions.CountAsync(r => r.DeletedAt == null && r.AssignedToId == uid
                    && (r.Status == RequisitionStatus.Submitted || r.Status == RequisitionStatus.Approved)
                    && !linkedPos.Contains(r.Id));
                mine += await db.MaterialRequisitions.CountAsync(r => r.AssignedToId == uid
                    && (r.Status == MaterialRequisitionStatus.Submitted || r.Status == MaterialRequisitionStatus.PurchaseRoute));
                if (mine > 0) alerts.Add(new
                {
                    kind = "MINHAS_DEMANDAS", severity = "alta", count = mine, view = "triage",
                    text = $"{mine} demanda(s) designada(s) a você aguardando continuidade.",
                });
            }

            // Demandas de compra + pedidos emitidos há mais de 7 dias sem recebimento
            if (mods.Contains(AppModules.Compras) && PurchaseOrderService.CanManage(role))
            {
                var linked = db.PurchaseOrders.Where(o => o.SourcePrId != null && o.Status != PurchaseOrderStatus.Cancelled).Select(o => o.SourcePrId!.Value);
                var demands = await db.Requisitions.CountAsync(r =>
                    (r.Status == RequisitionStatus.Submitted || r.Status == RequisitionStatus.Approved) && !linked.Contains(r.Id));
                var routeItems = await db.MaterialRequisitionItems.CountAsync(i => i.Status == MaterialItemStatus.PurchaseRoute);
                if (demands + routeItems > 0) alerts.Add(new
                {
                    kind = "DEMANDA", severity = "media", count = demands + routeItems, view = "buy-demands",
                    text = $"{demands} requisição(ões) aprovada(s) e {routeItems} item(ns) em rota de compra aguardando pedido.",
                });
                if (TriageService.CanTriage(role))
                {
                    var untriaged = await db.Requisitions.CountAsync(r => r.DeletedAt == null
                        && (r.Status == RequisitionStatus.Submitted || r.Status == RequisitionStatus.Approved)
                        && r.AssignedToId == null && !linked.Contains(r.Id));
                    untriaged += await db.MaterialRequisitions.CountAsync(r => r.AssignedToId == null
                        && (r.Status == MaterialRequisitionStatus.Submitted || r.Status == MaterialRequisitionStatus.PurchaseRoute));
                    if (untriaged > 0) alerts.Add(new
                    {
                        kind = "TRIAGEM", severity = "media", count = untriaged, view = "triage",
                        text = $"{untriaged} demanda(s) sem responsável designado na triagem.",
                    });
                }
                var lateLimit = clock.GetUtcNow().AddDays(-7);
                var latePos = await db.PurchaseOrders.CountAsync(o => o.Status == PurchaseOrderStatus.Issued && o.CreatedAt < lateLimit);
                if (latePos > 0) alerts.Add(new
                {
                    kind = "PO_ATRASO", severity = "alta", count = latePos, view = "buy-orders",
                    text = $"{latePos} pedido(s) de compra emitido(s) há mais de 7 dias sem recebimento.",
                });
            }

            // Contratos e certidões (V2-P3): vencimentos avisados na Central de Avisos
            if ((mods.Contains(AppModules.Contratos) || mods.Contains(AppModules.Fornecedores))
                && (PurchaseOrderService.CanManage(role) || role == Roles.Auditor))
            {
                var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
                var contratos = await db.Suppliers
                    .Where(s => s.Active && s.ContractValidUntil != null && s.ContractItems.Any())
                    .Select(s => new { s.ContractNumber, s.ContractValidUntil }).ToListAsync();
                var vencidos = contratos.Count(c => c.ContractValidUntil < hoje);
                if (vencidos > 0) alerts.Add(new
                {
                    kind = "CONTRATO_VENCIDO", severity = "alta", count = vencidos, view = "contracts",
                    text = $"{vencidos} contrato(s) de parceria vencido(s) — renove ou encerre.",
                });
                var d30 = contratos.Count(c => c.ContractValidUntil >= hoje && c.ContractValidUntil <= hoje.AddDays(30));
                var d60 = contratos.Count(c => c.ContractValidUntil > hoje.AddDays(30) && c.ContractValidUntil <= hoje.AddDays(60));
                var d90 = contratos.Count(c => c.ContractValidUntil > hoje.AddDays(60) && c.ContractValidUntil <= hoje.AddDays(90));
                if (d30 + d60 + d90 > 0) alerts.Add(new
                {
                    kind = "CONTRATO_VENCENDO", severity = d30 > 0 ? "alta" : d60 > 0 ? "media" : "info",
                    count = d30 + d60 + d90, view = "contracts",
                    text = $"Contrato(s) de parceria vencendo: {d30} em 30 dias, {d60} em 60, {d90} em 90.",
                });
                var certidoes = await db.SupplierDocuments.Where(d => d.ValidUntil != null)
                    .Select(d => d.ValidUntil!.Value).ToListAsync();
                var certVencidas = certidoes.Count(v => v < hoje);
                if (certVencidas > 0) alerts.Add(new
                {
                    kind = "CERTIDAO_VENCIDA", severity = "alta", count = certVencidas, view = "suppliers",
                    text = $"{certVencidas} certidão(ões) de fornecedor vencida(s) — a homologação fica RESTRITA até regularizar.",
                });
                var certVencendo = certidoes.Count(v => v >= hoje && v <= hoje.AddDays(30));
                if (certVencendo > 0) alerts.Add(new
                {
                    kind = "CERTIDAO_VENCENDO", severity = "media", count = certVencendo, view = "suppliers",
                    text = $"{certVencendo} certidão(ões) de fornecedor vencendo nos próximos 30 dias.",
                });
            }

            // Processo de cotação (RFQ-001): cada etapa avisa o responsável da vez
            if (mods.Contains(AppModules.Compras) || mods.Contains(AppModules.Aprovacao))
            {
                if (QuotationService.CanConduct(role))
                {
                    var open = await db.Quotations.CountAsync(q => q.Status == QuotationStatus.Open);
                    if (open > 0) alerts.Add(new
                    {
                        kind = "COTACAO_ABERTA", severity = "info", count = open, view = "quotations",
                        text = $"{open} cotação(ões) aberta(s) aguardando propostas.",
                    });
                    var analysis = await db.Quotations.CountAsync(q => q.Status == QuotationStatus.Analysis);
                    if (analysis > 0) alerts.Add(new
                    {
                        kind = "COTACAO_ANALISE", severity = "media", count = analysis, view = "quotations",
                        text = $"{analysis} cotação(ões) em análise aguardando a escolha do fornecedor.",
                    });
                    var toIssue = await db.Quotations.CountAsync(q => q.Status == QuotationStatus.ApprovedForIssue);
                    if (toIssue > 0) alerts.Add(new
                    {
                        kind = "OC_EMITIR", severity = "alta", count = toIssue, view = "quotations",
                        text = $"{toIssue} processo(s) aprovado(s) aguardando a emissão da ordem de compra.",
                    });
                }
                if (QuotationService.CanApproveAsManager(role))
                {
                    var mgrQuery = db.Quotations.Where(q => q.Status == QuotationStatus.AwaitingManager && q.SelectedBy != uid);
                    if (role == Roles.Approver)
                    {
                        // Pleno: só processos dos CCs sob a sua gerência
                        var managedCodes = await db.CostCenters.Where(c => c.Active && c.ManagerUserId == uid)
                            .Select(c => c.Code).ToListAsync();
                        mgrQuery = managedCodes.Count == 0
                            ? mgrQuery.Where(_ => false)
                            : mgrQuery.Where(q => managedCodes.Contains(q.CostCenter.ToUpper()));
                    }
                    var mgr = await mgrQuery.CountAsync();
                    if (mgr > 0) alerts.Add(new
                    {
                        kind = "APROVACAO_GERENTE", severity = "media", count = mgr, view = "pr-approvals",
                        text = $"{mgr} processo(s) de compra aguardando a sua aprovação gerencial.",
                    });
                }
                if (QuotationService.CanApproveAsDirector(role))
                {
                    var awaiting = await db.Quotations.Where(q => q.Status == QuotationStatus.AwaitingDirector
                        && q.SelectedBy != uid && q.ManagerApprovedBy != uid)
                        .Select(q => new { q.CostCenter, q.ManagerApprovedBy }).ToListAsync();
                    var dir = awaiting.Count;
                    if (role == Roles.Director && dir > 0)
                    {
                        // Diretor: só processos roteados a ele (via gerente→diretor) ou sem roteamento
                        var ccCodes = awaiting.Select(a => a.CostCenter.ToUpperInvariant()).Distinct().ToList();
                        var ccManagers = await db.CostCenters
                            .Where(c => c.Active && ccCodes.Contains(c.Code) && c.ManagerUserId != null)
                            .ToDictionaryAsync(c => c.Code, c => c.ManagerUserId!.Value);
                        var managerIds = awaiting.Select(a => a.ManagerApprovedBy).OfType<Guid>()
                            .Concat(ccManagers.Values).Distinct().ToList();
                        var directorOf = await db.Users.Where(u => managerIds.Contains(u.Id))
                            .ToDictionaryAsync(u => u.Id, u => u.DirectorId);
                        dir = awaiting.Count(a =>
                        {
                            var managerId = a.ManagerApprovedBy
                                ?? (ccManagers.TryGetValue(a.CostCenter.ToUpperInvariant(), out var m) ? m : (Guid?)null);
                            var linked = managerId is { } mid && directorOf.TryGetValue(mid, out var d) ? d : null;
                            return linked is null || linked == uid;
                        });
                    }
                    if (dir > 0) alerts.Add(new
                    {
                        kind = "APROVACAO_DIRETOR", severity = "media", count = dir, view = "pr-approvals",
                        text = $"{dir} processo(s) de compra aguardando a aprovação da diretoria.",
                    });
                }
            }

            // INTEL-C: achado de severidade alta não pode ficar esperando alguém
            // abrir a tela de Insights. Ele entra na Central de Avisos como os
            // outros — com a mesma cara — e assim também conta no menu.
            if (InsightsService.CanView(role) && mods.Contains(AppModules.Insights))
            {
                var altos = (await insights.FindInsightsAsync(6)).Where(i => i.Severity == "alta").ToList();
                foreach (var g in altos.GroupBy(i => i.Code).OrderBy(g => g.Key))
                    alerts.Add(new
                    {
                        kind = $"INSIGHT_{g.Key.Replace("-", "_")}", severity = "alta", count = g.Count(),
                        view = g.First().View ?? "insights",
                        text = g.Count() == 1
                            ? $"{g.First().Title}. {g.First().Action}"
                            : $"{g.Count()} achados de {RotuloDoAchado(g.Key)} — veja em Insights & Executivo.",
                        // achado é constatação, não fila: aparece no aviso mas não
                        // entra no contador do menu, que conta trabalho parado. Sem
                        // isso o menu diria "Pedidos 3" com a lista de pedidos vazia.
                        counts = false,
                    });
            }

            return Ok(new { alerts, generatedAt = clock.GetUtcNow() }, ctx);
        }).RequireAuthorization();
    }
}
