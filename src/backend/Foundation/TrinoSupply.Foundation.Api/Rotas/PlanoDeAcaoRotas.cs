using System.Security.Claims;
using TrinoSupply.Foundation.Api.Acoes;
using TrinoSupply.Foundation.Api.Domain;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// O plano de ação — o projeto, com as ações dentro dele.
///
/// <para>
/// O acesso é pelo módulo <c>PLANO_ACAO</c>, que o administrador concede no cadastro de
/// usuários — e não por papel. É ferramenta de trabalho de qualquer área, não de um cargo:
/// amarrá-la a papel deixaria de fora justamente quem a área escolheu para tocar.
/// </para>
///
/// <para>
/// <b>Encerrar e reabrir têm rota própria</b>, e não são campo que se edita de passagem:
/// encerrar é decisão com dono, data e evidência, e reabrir é do administrador.
/// </para>
/// </summary>
public static class PlanoDeAcaoRotas
{
    public static void MapPlanoDeAcao(this WebApplication app)
    {
        var planos = app.MapGroup("/api/v1/action-plans").RequireAuthorization();
        planos.AddEndpointFilter(RejectSupplierRole());
        planos.AddEndpointFilter(RequireModules(AppModules.PlanoAcao));

        static object Acao(ActionItem a, DateOnly hoje) => new
        {
            id = a.Id, number = a.Number, seq = a.Seq, planId = a.PlanId, title = a.Title,
            reason = a.Reason, area = a.Area, supportArea = a.SupportArea,
            responsibleId = a.ResponsibleId, responsibleLabel = a.ResponsibleLabel,
            startDate = a.StartDate, dueDate = a.DueDate,
            expectedGain = a.ExpectedGain, realizedGain = a.RealizedGain,
            expectedResult = a.ExpectedResult, kpi = a.Kpi, costCenter = a.CostCenter,
            rootCauseRef = a.RootCauseRef, complexity = a.Complexity, riskLevel = a.RiskLevel,
            dependencies = a.Dependencies, evidence = a.Evidence, comments = a.Comments,
            status = a.Status, statusReason = a.StatusReason,
            // derivados: a tela não recalcula o que o servidor já sabe, senão as duas discordam
            progress = PlanoDeAcao.ProgressoReal(a),
            late = PlanoDeAcao.Atrasada(a, hoje),
            daysLate = PlanoDeAcao.DiasDeAtraso(a, hoje),
            open = PlanoDeAcao.Aberta(a),
            completedAt = a.CompletedAt, createdByLabel = a.CreatedByLabel, createdAt = a.CreatedAt,
        };

        static object Resumo(ActionPlan p, DateOnly hoje) => new
        {
            id = p.Id, code = p.Code, title = p.Title, description = p.Description,
            costCenter = p.CostCenter,
            areas = (p.Areas ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            otherArea = p.OtherArea,
            priority = p.Priority, criticality = p.Criticality, complexity = p.Complexity,
            category = p.Category, sponsor = p.Sponsor, managerName = p.ManagerName,
            startDate = p.StartDate, dueDate = p.DueDate, completion = p.Completion,
            problem = p.Problem, businessReason = p.BusinessReason,
            operationalImpact = p.OperationalImpact, financialImpact = p.FinancialImpact,
            kpiAffected = p.KpiAffected, targetGoal = p.TargetGoal,
            roiExpected = p.RoiExpected,
            savingExpected = p.SavingExpected, savingRealized = p.SavingRealized,
            investmentPlanned = p.InvestmentPlanned, investmentActual = p.InvestmentActual,
            cancelled = p.Cancelled, cancelReason = p.CancelReason,
            life = p.Life, closedAt = p.ClosedAt, closedByLabel = p.ClosedByLabel,
            evidenceNote = p.EvidenceNote, cycleId = p.CycleId, autoKey = p.AutoKey,
            responsibles = p.Responsibles.Select(r => new { userId = r.UserId, label = r.UserLabel }),
            // derivados, num lugar só: a lista, o card e o relatório respondem igual
            status = PlanoDoPlano.Situacao(p, hoje),
            itemsProgress = PlanoDoPlano.ProgressoDosItens(p),
            savingTotalExpected = PlanoDoPlano.SavingEsperado(p),
            savingTotalRealized = PlanoDoPlano.SavingRealizado(p),
            roi = PlanoDoPlano.Roi(p),
            closedWithPending = PlanoDoPlano.EncerradoComPendencia(p),
            itemCount = p.Items.Count,
            openItems = p.Items.Count(PlanoDeAcao.Aberta),
            createdByLabel = p.CreatedByLabel, createdAt = p.CreatedAt,
        };

        static object Risco(PlanRisk r) => new
        {
            id = r.Id, description = r.Description,
            probability = r.Probability, impact = r.Impact,
            // a severidade é calculada, nunca escolhida: dois riscos "altos" que significam
            // coisas diferentes é o que faz a matriz deixar de servir para priorizar
            score = SeveridadeDoRisco.Pontos(r), severity = SeveridadeDoRisco.Rotulo(r),
            mitigation = r.Mitigation,
            responsibleId = r.ResponsibleId, responsibleLabel = r.ResponsibleLabel,
            status = r.Status,
        };

        planos.MapGet("/", async (PlanoDeAcaoService svc, TimeProvider clock, HttpContext ctx,
            string? q, string? status, string? priority, string? costCenter, string? area,
            Guid? responsibleId, bool? closed, Guid? cycleId, CancellationToken ct) =>
        {
            var pagina = await svc.ListarAsync(new FiltroDePlanos(
                q, status, priority, costCenter, area, responsibleId, closed, cycleId), ct);
            var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            return Ok(new
            {
                items = pagina.Itens.Select(p => Resumo(p, hoje)),
                placar = pagina.Placar,
                filterOptions = new
                {
                    responsibles = pagina.Responsaveis.Select(r => new { id = r.Id, label = r.Label }),
                    costCenters = pagina.CentrosCusto,
                    areas = VocabularioDoPlano.Areas,
                    categories = VocabularioDoPlano.Categorias,
                    priorities = VocabularioDoPlano.Prioridades,
                    degrees = VocabularioDoPlano.Graus,
                    riskDegrees = SeveridadeDoRisco.Graus,
                    statuses = SituacaoDoPlano.Todas,
                },
            }, ctx);
        });

        planos.MapGet("/{id:guid}", async (Guid id, PlanoDeAcaoService svc, TimeProvider clock,
            HttpContext ctx, CancellationToken ct) =>
        {
            var plano = await svc.AbrirAsync(id, ct);
            if (plano is null) return Error(ctx, 404, "AP-ERR-404", "Plano não encontrado.");
            var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var causa = plano.RootCauses.FirstOrDefault();
            var licao = plano.Lessons.FirstOrDefault();
            return Ok(new
            {
                plan = Resumo(plano, hoje),
                items = plano.Items.OrderBy(i => i.Seq).Select(i => Acao(i, hoje)),
                risks = plano.Risks.OrderByDescending(SeveridadeDoRisco.Pontos).Select(Risco),
                rootCause = causa is null ? null : new
                {
                    method = causa.Method, contentJson = causa.ContentJson, mainCause = causa.MainCause,
                },
                lessons = licao is null ? null : new
                {
                    whatWorked = licao.WhatWorked, whatFailed = licao.WhatFailed,
                    lessons = licao.Lessons, bestPractice = licao.BestPractice,
                    nextSteps = licao.NextSteps, recommendation = licao.Recommendation,
                },
            }, ctx);
        });

        planos.MapPost("/", async (PlanoRequest body, PlanoDeAcaoService svc, TimeProvider clock,
            ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            if (!PlanoDeAcaoService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "AP-ERR-900", "Seu papel não mantém plano de ação.");
            var (plano, erro) = await svc.CriarPlanoAsync(BuildActor(p)!, body.Dados(), ct);
            if (erro is not null) return Error(ctx, 400, erro.Code, erro.Message);
            var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            return Results.Json(new { data = Resumo(plano!, hoje), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        planos.MapPatch("/{id:guid}", async (Guid id, PlanoRequest body, PlanoDeAcaoService svc,
            TimeProvider clock, ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            if (!PlanoDeAcaoService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "AP-ERR-900", "Seu papel não mantém plano de ação.");
            var (plano, erro) = await svc.AtualizarPlanoAsync(id, body.Dados(), ct);
            return erro is not null
                ? Error(ctx, erro.Code switch { "AP-ERR-404" => 404, "AP-ERR-020" => 409, _ => 400 },
                    erro.Code, erro.Message)
                : Ok(Resumo(plano!, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)), ctx);
        });

        // encerrar é decisão, e não consequência do progresso: um plano a 100% continua
        // ativo até alguém dizer que acabou
        planos.MapPost("/{id:guid}/close", async (Guid id, EncerrarPlanoRequest? body,
            PlanoDeAcaoService svc, TimeProvider clock, ClaimsPrincipal p, HttpContext ctx,
            CancellationToken ct) =>
        {
            if (!PlanoDeAcaoService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "AP-ERR-900", "Seu papel não mantém plano de ação.");
            var (plano, erro) = await svc.EncerrarAsync(BuildActor(p)!, id, body?.EvidenceNote, ct);
            return erro is not null
                ? Error(ctx, erro.Code == "AP-ERR-404" ? 404 : 400, erro.Code, erro.Message)
                : Ok(Resumo(plano!, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)), ctx);
        });

        planos.MapPost("/{id:guid}/reopen", async (Guid id, PlanoDeAcaoService svc,
            TimeProvider clock, ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            if (!PlanoDeAcaoService.CanReopen(RoleOf(p)))
                return Error(ctx, 403, "AP-ERR-901",
                    "Reabrir um plano encerrado é do gestor ou do administrador.");
            var (plano, erro) = await svc.ReabrirAsync(id, ct);
            return erro is not null
                ? Error(ctx, erro.Code == "AP-ERR-404" ? 404 : 400, erro.Code, erro.Message)
                : Ok(Resumo(plano!, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)), ctx);
        });

        // ---- as ações do plano ------------------------------------------------

        planos.MapPost("/{id:guid}/items", async (Guid id, CriarAcaoRequest body,
            PlanoDeAcaoService svc, TimeProvider clock, ClaimsPrincipal p, HttpContext ctx,
            CancellationToken ct) =>
        {
            if (!PlanoDeAcaoService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "AP-ERR-900", "Seu papel não mantém plano de ação.");
            var (acao, erro) = await svc.CriarAcaoAsync(BuildActor(p)!, id, body.Dados(), ct);
            if (erro is not null)
                return Error(ctx, erro.Code switch { "AP-ERR-404" => 404, "AP-ERR-020" => 409, _ => 400 },
                    erro.Code, erro.Message);
            var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            return Results.Json(new { data = Acao(acao!, hoje), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        planos.MapPatch("/items/{itemId:guid}", async (Guid itemId, AtualizarAcaoRequest body,
            PlanoDeAcaoService svc, TimeProvider clock, ClaimsPrincipal p, HttpContext ctx,
            CancellationToken ct) =>
        {
            if (!PlanoDeAcaoService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "AP-ERR-900", "Seu papel não mantém plano de ação.");
            var (acao, erro) = await svc.AtualizarAcaoAsync(itemId, body.Dados(), body.RealizedGain, ct);
            return erro is not null
                ? Error(ctx, erro.Code switch { "AC-ERR-404" => 404, "AP-ERR-020" => 409, _ => 400 },
                    erro.Code, erro.Message)
                : Ok(Acao(acao!, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)), ctx);
        });

        // situação tem rota própria: suspender e cancelar exigem motivo, e misturar isso na
        // edição comum deixaria a ação parar sem ninguém assumir a decisão
        planos.MapPost("/items/{itemId:guid}/status", async (Guid itemId, MudarStatusDaAcaoRequest body,
            PlanoDeAcaoService svc, TimeProvider clock, ClaimsPrincipal p, HttpContext ctx,
            CancellationToken ct) =>
        {
            if (!PlanoDeAcaoService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "AP-ERR-900", "Seu papel não mantém plano de ação.");
            var (acao, erro) = await svc.MudarStatusAsync(itemId, body.Status, body.Reason, body.Progress, ct);
            return erro is not null
                ? Error(ctx, erro.Code switch { "AC-ERR-404" => 404, "AP-ERR-020" => 409, _ => 400 },
                    erro.Code, erro.Message)
                : Ok(Acao(acao!, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)), ctx);
        });

        // ---- riscos, causa raiz e lições ---------------------------------------

        planos.MapPut("/{id:guid}/risks", async (Guid id, RiscoDoPlanoRequest body,
            PlanoDeAcaoService svc, ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            if (!PlanoDeAcaoService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "AP-ERR-900", "Seu papel não mantém plano de ação.");
            var (risco, erro) = await svc.SalvarRiscoAsync(id, body.Id, body.Description ?? "",
                body.Probability, body.Impact, body.Mitigation, body.ResponsibleId, body.Status, ct);
            return erro is not null
                ? Error(ctx, erro.Code switch { "AP-ERR-404" => 404, "AP-ERR-020" => 409, _ => 400 },
                    erro.Code, erro.Message)
                : Ok(Risco(risco!), ctx);
        });

        planos.MapDelete("/{id:guid}/risks/{riskId:guid}", async (Guid id, Guid riskId,
            PlanoDeAcaoService svc, ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            if (!PlanoDeAcaoService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "AP-ERR-900", "Seu papel não mantém plano de ação.");
            var erro = await svc.ApagarRiscoAsync(id, riskId, ct);
            return erro is not null
                ? Error(ctx, erro.Code == "AP-ERR-404" ? 404 : 409, erro.Code, erro.Message)
                : Ok(new { deleted = true }, ctx);
        });

        planos.MapPut("/{id:guid}/root-cause", async (Guid id, CausaRaizDoPlanoRequest body,
            PlanoDeAcaoService svc, ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            if (!PlanoDeAcaoService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "AP-ERR-900", "Seu papel não mantém plano de ação.");
            var (causa, erro) = await svc.SalvarCausaRaizAsync(id, body.Method, body.ContentJson, body.MainCause, ct);
            return erro is not null
                ? Error(ctx, erro.Code == "AP-ERR-404" ? 404 : 409, erro.Code, erro.Message)
                : Ok(new { method = causa!.Method, contentJson = causa.ContentJson, mainCause = causa.MainCause }, ctx);
        });

        planos.MapPut("/{id:guid}/lessons", async (Guid id, LicoesDoPlanoRequest body,
            PlanoDeAcaoService svc, ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            if (!PlanoDeAcaoService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "AP-ERR-900", "Seu papel não mantém plano de ação.");
            var (licao, _) = await svc.SalvarLicoesAsync(id, body.WhatWorked, body.WhatFailed,
                body.Lessons, body.BestPractice, body.NextSteps, body.Recommendation, ct);
            return Ok(new
            {
                whatWorked = licao!.WhatWorked, whatFailed = licao.WhatFailed,
                lessons = licao.Lessons, bestPractice = licao.BestPractice,
                nextSteps = licao.NextSteps, recommendation = licao.Recommendation,
            }, ctx);
        });
    }
}
