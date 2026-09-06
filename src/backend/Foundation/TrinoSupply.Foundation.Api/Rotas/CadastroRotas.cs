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
/// Os cadastros que sustentam o resto: centros de custo (com o gerente
/// responsável, que define a alçada), a triagem de demandas e as empresas do
/// grupo. Junto vem o seletor de usuários que essas telas usam.
///
/// Saiu do Program.cs no ARQ-A. O conteúdo é o mesmo — grupo, filtros e
/// handlers na mesma ordem —, só mudou de arquivo.
/// </summary>
public static class CadastroRotas
{
    public static void MapCadastros(this WebApplication app)
    {
        // ---- Centros de custo (master data mínimo — dimensões dos dashboards) --------
        static object CcView(CostCenter c) => new
        {
            id = c.Id, code = c.Code, name = c.Name, region = c.Region,
            companyId = c.CompanyId,
            managerUserId = c.ManagerUserId, managerName = c.ManagerName,
            clientName = c.ClientName, active = c.Active,
            level1ValueLimit = c.Level1ValueLimit, level2ValueLimit = c.Level2ValueLimit,
            // alçadas do centro: qualquer pessoa do nível resolve a etapa
            level1 = c.Approvers.Where(a => a.Level == ApprovalLevels.Level1)
                .Select(a => new { userId = a.UserId, name = a.UserName }).ToList(),
            level2 = c.Approvers.Where(a => a.Level == ApprovalLevels.Level2)
                .Select(a => new { userId = a.UserId, name = a.UserName }).ToList(),
        };

        // usuários ativos (id + nome + papel) para pickers de vínculo — sem dados sensíveis
        app.MapGet("/api/v1/users/pickers", async (AppDbContext db, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!CostCenterService.CanMaintain(RoleOf(p)) && RoleOf(p) != Roles.SystemAdministrator)
                return Error(ctx, 403, "IAM-ERR-018", "Seu papel não acessa a lista de usuários.");
            return Ok(new
            {
                items = await db.Users.Where(u => u.Active).OrderBy(u => u.Name)
                    .Select(u => new { id = u.Id, name = u.Name, role = u.Role }).ToListAsync(),
            }, ctx);
        }).RequireAuthorization().AddEndpointFilter(RejectSupplierRole());

        var ccs = app.MapGroup("/api/v1/cost-centers").RequireAuthorization();
        ccs.AddEndpointFilter(RejectSupplierRole());

        // listagem aberta a autenticados: os formulários de requisição/solicitação usam o picker
        ccs.MapGet("/", async (CostCenterService svc, ClaimsPrincipal p, HttpContext ctx, bool? all) =>
        {
            var includeInactive = all == true && CostCenterService.CanMaintain(RoleOf(p));
            return Ok(new { items = (await svc.ListAsync(includeInactive)).Select(CcView) }, ctx);
        });

        ccs.MapPost("/", async (CreateCostCenterRequest body, CostCenterService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!CostCenterService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.CentrosCusto))
                return Error(ctx, 403, "CC-ERR-900", "Seu usuário não mantém centros de custo.");
            var (cc, error) = await svc.CreateAsync(ActorId(p), body.Code, body.Name, body.Region, body.ManagerUserId, body.ClientName, body.CompanyId, body.Level1UserIds, body.Level2UserIds, body.Level1ValueLimit, body.Level2ValueLimit);
            return error is not null ? Error(ctx, 400, error.Code, error.Message)
                : Results.Json(new { data = CcView(cc!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        ccs.MapPatch("/{id:guid}", async (Guid id, UpdateCostCenterRequest body, CostCenterService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!CostCenterService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.CentrosCusto))
                return Error(ctx, 403, "CC-ERR-900", "Seu usuário não mantém centros de custo.");
            var (cc, error) = await svc.UpdateAsync(id, body.Name, body.Region, body.ManagerUserId, body.ClientName, body.Active, body.CompanyId, body.Level1UserIds, body.Level2UserIds, body.Level1ValueLimit, body.Level2ValueLimit, body.ClearValueLimits == true);
            return error is not null ? Error(ctx, error.Code == "CC-ERR-404" ? 404 : 400, error.Code, error.Message)
                : Ok(CcView(cc!), ctx);
        });

        // ---- Triagem de demandas (tickets) ------------------------------------------
        static object TicketView(TriageTicket t) => new
        {
            kind = t.Kind, id = t.Id, number = t.Number, costCenter = t.CostCenter,
            requesterLabel = t.RequesterLabel, summary = t.Summary, estimatedValue = t.EstimatedValue,
            openedAt = t.OpenedAt, status = t.Status,
            processStatus = t.Process?.Key, processStatusLabel = t.Process?.Label,
            processStatusTone = t.Process?.Tone, processStatusHint = t.Process?.Explanation,
            splitProcesses = t.SplitProcesses,   // itens em processos diferentes: situação por linha
            priority = t.Priority, neededBy = t.NeededBy, justification = t.Justification,
            urgencyReason = t.UrgencyReason, urgencyImpact = t.UrgencyImpact,
            priorityChangedByLabel = t.PriorityChangedByLabel, priorityChangeReason = t.PriorityChangeReason,
            items = (t.Items ?? []).Select(i => new
            {
                id = i.Id, sequence = i.Sequence, code = i.Code, description = i.Description,
                size = i.Size, quantity = i.Quantity, unitOfMeasure = i.UnitOfMeasure, notes = i.Notes,
                family = i.Family, quotationNumber = i.QuotationNumber, purchaseOrderNumber = i.PurchaseOrderNumber,
                processStatus = i.Process?.Key, processStatusLabel = i.Process?.Label,
                processStatusTone = i.Process?.Tone, processStatusHint = i.Process?.Explanation,
            }),
            assignedToId = t.AssignedToId, assignedToLabel = t.AssignedToLabel,
            assignedByLabel = t.AssignedByLabel, assignedAt = t.AssignedAt,
        };

        var triage = app.MapGroup("/api/v1/triage").RequireAuthorization();
        triage.AddEndpointFilter(RejectSupplierRole());

        triage.MapGet("/", async (TriageService svc, ClaimsPrincipal p, HttpContext ctx,
            string? filter, string? requester, string? assignee, string? status) =>
        {
            if (BuildActor(p) is not { } actor) return Error(ctx, 403, "TRI-ERR-900", "Seu papel não acessa a triagem.");
            var scope = (filter ?? "TODAS").Trim().ToUpperInvariant();
            if (scope != "MINHAS" && !TriageService.CanTriage(RoleOf(p)) && !PurchaseOrderService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "TRI-ERR-900", "Seu papel não acessa o painel de demandas.");

            var all = await svc.ListAsync(actor, scope);
            // filtros da tela: solicitante, comprador responsável e situação do fluxo
            var items = all.Where(t =>
                (string.IsNullOrWhiteSpace(requester) || t.RequesterLabel == requester)
                && (string.IsNullOrWhiteSpace(assignee)
                    || (assignee == "SEM" ? t.AssignedToId is null : t.AssignedToLabel == assignee))
                && (string.IsNullOrWhiteSpace(status) || t.Process?.Key == status)).ToList();

            return Ok(new
            {
                items = items.Select(TicketView),
                total = all.Count,
                requesters = all.Select(t => t.RequesterLabel).Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct().OrderBy(x => x),
                assignees = all.Where(t => !string.IsNullOrWhiteSpace(t.AssignedToLabel))
                    .Select(t => t.AssignedToLabel!).Distinct().OrderBy(x => x),
                statuses = all.Where(t => t.Process is not null)
                    .Select(t => new { key = t.Process!.Key, label = t.Process.Label })
                    .DistinctBy(x => x.key).OrderBy(x => x.label),
            }, ctx);
        });

        triage.MapGet("/responsibles", async (TriageService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!TriageService.CanTriage(RoleOf(p)))
                return Error(ctx, 403, "TRI-ERR-900", "Seu papel não distribui demandas.");
            return Ok(new { items = await svc.ResponsiblesAsync() }, ctx);
        });

        triage.MapPost("/assign", async (AssignTicketRequest body, TriageService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (BuildActor(p) is not { } actor) return Error(ctx, 403, "TRI-ERR-900", "Seu papel não acessa a triagem.");
            var (ticket, error) = await svc.AssignAsync(actor, body.Kind ?? "", body.Id, body.ResponsibleId);
            return error is not null
                ? Error(ctx, error.Code switch { "TRI-ERR-404" => 404, "TRI-ERR-900" => 403, "TRI-ERR-020" => 409, _ => 400 },
                        error.Code, error.Message)
                : Ok(TicketView(ticket!), ctx);
        });

        // redistribuição em lote (V2-P3): várias demandas para o mesmo responsável de uma vez
        triage.MapPost("/assign-batch", async (AssignBatchRequest body, TriageService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (BuildActor(p) is not { } actor) return Error(ctx, 403, "TRI-ERR-900", "Seu papel não acessa a triagem.");
            var alvos = body.Items ?? [];
            if (alvos.Count == 0) return Error(ctx, 400, "TRI-ERR-010", "Selecione ao menos uma demanda.");
            var okCount = 0;
            var falhas = new List<object>();
            foreach (var alvo in alvos)
            {
                var (_, error) = await svc.AssignAsync(actor, alvo.Kind ?? "", alvo.Id, body.ResponsibleId);
                if (error is null) okCount++;
                else falhas.Add(new { alvo.Id, code = error.Code, message = error.Message });
            }
            return Ok(new { assigned = okCount, failed = falhas }, ctx);
        });

        // alteração de prioridade com justificativa (V2-P3)
        triage.MapPost("/priority", async (ChangePriorityRequest body, TriageService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (BuildActor(p) is not { } actor) return Error(ctx, 403, "TRI-ERR-900", "Seu papel não acessa a triagem.");
            var (pr, error) = await svc.ChangePriorityAsync(actor, body.Id, body.Priority ?? "", body.Reason, body.Impact);
            return error is not null
                ? Error(ctx, error.Code switch { "TRI-ERR-404" => 404, "TRI-ERR-900" => 403, "TRI-ERR-020" => 409, _ => 422 },
                        error.Code, error.Message)
                : Ok(new { id = pr!.Id, priority = pr.Priority, reason = pr.PriorityChangeReason,
                           byLabel = pr.PriorityChangedByLabel, at = pr.PriorityChangedAt }, ctx);
        });

        // ---- Empresas do grupo (CNPJs) — cada CC pode apontar para um CNPJ -----------
        static object CompanyView(Company c) => new
        {
            id = c.Id, legalName = c.LegalName, taxId = c.TaxId, stateRegistration = c.StateRegistration,
            address = c.Address, district = c.District, city = c.City, state = c.State, zip = c.Zip,
            phone = c.Phone, email = c.Email, active = c.Active,
        };

        var companies = app.MapGroup("/api/v1/companies").RequireAuthorization();
        companies.AddEndpointFilter(RejectSupplierRole());

        // listagem aberta a autenticados internos: SC/CC usam o picker de empresa
        companies.MapGet("/", async (CompanyService svc, ClaimsPrincipal p, HttpContext ctx, bool? all) =>
        {
            var includeInactive = all == true && CompanyService.CanMaintain(RoleOf(p));
            return Ok(new { items = (await svc.ListAsync(includeInactive)).Select(CompanyView) }, ctx);
        });

        companies.MapPost("/", async (CreateCompanyRequest body, CompanyService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!CompanyService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.CentrosCusto))
                return Error(ctx, 403, "EMP-ERR-900", "Seu usuário não mantém o cadastro de CNPJs do grupo.");
            var (company, error) = await svc.CreateAsync(body.LegalName, body.TaxId, body.StateRegistration,
                body.Address, body.District, body.City, body.State, body.Zip, body.Phone, body.Email);
            return error is not null ? Error(ctx, error.Code == "EMP-ERR-012" ? 409 : 400, error.Code, error.Message)
                : Results.Json(new { data = CompanyView(company!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        companies.MapPatch("/{id:guid}", async (Guid id, UpdateCompanyRequest body, CompanyService svc, ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!CompanyService.CanMaintain(RoleOf(p)) || !ModulesOf(p).Contains(AppModules.CentrosCusto))
                return Error(ctx, 403, "EMP-ERR-900", "Seu usuário não mantém o cadastro de CNPJs do grupo.");
            var (company, error) = await svc.UpdateAsync(id, body.LegalName, body.StateRegistration, body.Address,
                body.District, body.City, body.State, body.Zip, body.Phone, body.Email, body.Active);
            return error is not null ? Error(ctx, error.Code == "EMP-ERR-404" ? 404 : 400, error.Code, error.Message)
                : Ok(CompanyView(company!), ctx);
        });
    }
}
