using System.Security.Claims;
using TrinoSupply.Foundation.Api.Acoes;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Melhoria;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// O ciclo de melhoria (PDCA) — onde se trata a causa.
///
/// <para>
/// O acesso é o módulo <c>PDCA</c>, que o administrador concede no cadastro, como o plano de
/// ação. Dentro do módulo, quem vê o quê é a visibilidade do serviço, e não o papel: a mesma
/// função responde à lista e à abertura do ciclo.
/// </para>
///
/// <para>
/// <b>Encerrar, reabrir e mudar o escopo têm rota própria.</b> Não é organização: o
/// encerramento carrega o veredito e a confirmação de pendências, e a mudança de escopo leva
/// as ações junto — nenhum dos dois é um campo que se edita de passagem.
/// </para>
/// </summary>
public static class MelhoriaRotas
{
    public static void MapMelhoria(this WebApplication app)
    {
        var ciclos = app.MapGroup("/api/v1/improvement-cycles").RequireAuthorization();
        ciclos.AddEndpointFilter(RejectSupplierRole());
        ciclos.AddEndpointFilter(RequireModules(AppModules.Pdca));

        static object Resumo(ImprovementCycle c) => new
        {
            id = c.Id, code = c.Code, title = c.Title,
            scope = c.Scope, scopeLabel = EscopoDoCiclo.Rotulo(c.Scope),
            phase = c.Phase, phaseLabel = FaseDoCiclo.Rotulo(c.Phase),
            region = c.Region, sectorId = c.SectorId, areas = c.Areas, priority = c.Priority,
            ownerId = c.OwnerId, ownerLabel = c.OwnerLabel,
            indicator = c.Indicator, baseline = c.Baseline, goalValue = c.GoalValue,
            unit = c.Unit, goalDeadline = c.GoalDeadline, resultValue = c.ResultValue,
            goalMet = c.GoalMet,
            startDate = c.StartDate, endDate = c.EndDate,
            closedAt = c.ClosedAt, closedByLabel = c.ClosedByLabel, closedReason = c.ClosedReason,
            createdByLabel = c.CreatedByLabel, createdAt = c.CreatedAt,
        };

        static object Analise(AnaliseDeCausa a) => new
        {
            key = a.Chave, name = a.Nome, problem = a.Problema, effect = a.Efeito,
            rootCause = a.CausaRaiz, warning = a.Aviso,
            rows = (a.Linhas ?? []).Select(l => new
            {
                what = l.OQue, why = l.PorQue, where = l.Onde, when = l.Quando,
                who = l.Quem, how = l.Como, howMuch = l.Quanto,
            }),
            before = a.Antes, after = a.Depois, result = a.Resultado,
            currentFlow = a.FluxoAtual ?? [], proposedFlow = a.FluxoProposto ?? [],
            steps = a.Degraus.Select(d => new { number = d.Numero, question = d.Pergunta, answer = d.Resposta }),
            groups = a.Grupos.Select(g => new { key = g.Chave, label = g.Rotulo, items = g.Itens }),
            ideas = a.Ideias,
            causes = a.Causas.Select(c => new
            {
                label = c.Rotulo, value = c.Valor, percent = c.Percentual,
                cumulative = c.Acumulado, vital = c.Vital, detail = c.Detalhe,
            }),
        };

        static object Acao(ActionItem a, DateOnly hoje) => new
        {
            id = a.Id, number = a.Number, title = a.Title,
            responsibleId = a.ResponsibleId, responsibleLabel = a.ResponsibleLabel,
            dueDate = a.DueDate, status = a.Status, rootCauseRef = a.RootCauseRef,
            progress = PlanoDeAcao.ProgressoReal(a),
            late = PlanoDeAcao.Atrasada(a, hoje),
            open = PlanoDeAcao.Aberta(a),
        };

        ciclos.MapGet("/", async (CicloDeMelhoriaService svc, ClaimsPrincipal p, HttpContext ctx,
            string? q, string? phase, string? scope, CancellationToken ct) =>
        {
            var (itens, placar) = await svc.ListarAsync(
                BuildActor(p)!, new FiltroDeCiclos(q, phase, scope), ct);
            return Ok(new
            {
                items = itens.Select(Resumo),
                placar,
                options = new
                {
                    phases = FaseDoCiclo.Todas.Select(f => new { key = f, label = FaseDoCiclo.Rotulo(f) }),
                    scopes = EscopoDoCiclo.Todos.Select(e => new { key = e, label = EscopoDoCiclo.Rotulo(e) }),
                    tools = FerramentaDeCausa.Todas.Select(t => new
                    {
                        key = t, label = FerramentaDeCausa.Rotulo(t), hint = FerramentaDeCausa.ParaQue(t),
                    }),
                },
            }, ctx);
        });

        ciclos.MapGet("/{id:guid}", async (Guid id, CicloDeMelhoriaService svc, TimeProvider clock,
            ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            var completo = await svc.AbrirAsync(BuildActor(p)!, id, ct);
            // quem não enxerga o ciclo recebe 404, e não 403: dizer "existe, mas não é para
            // você" já entrega que ele existe, e a lista de quem não vê é a regra do módulo
            if (completo is null) return Error(ctx, 404, "PDCA-ERR-404", "Ciclo não encontrado.");
            var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var c = completo.Ciclo;
            return Ok(new
            {
                cycle = Resumo(c),
                plan = new
                {
                    problem = c.Problem, currentSituation = c.CurrentSituation,
                    causeAnalysis = c.CauseAnalysis, rootCause = c.RootCause,
                    goalDescription = c.GoalDescription,
                },
                check = new { checkedOn = c.CheckedOn, checkAnalysis = c.CheckAnalysis },
                act = new { standardization = c.Standardization, lessons = c.Lessons, newCycle = c.NewCycle },
                sectorName = completo.SetorNome,
                leader = c.Leader, mentor = c.Mentor, participants = c.Participants,
                annualSaving = c.AnnualSaving,
                watchers = c.Watchers.Select(w => new { userId = w.UserId, label = w.UserLabel }),
                costCenters = c.CostCenters.Select(x => x.CostCenter),
                // várias ferramentas convivem na mesma folha: obrigar a escolher uma faria
                // a análise contar meia história
                tools = c.Tools.OrderBy(t => t.Seq)
                    .Select(t => new { tool = t.ToolType, data = t.ToolData, seq = t.Seq }),
                analyses = completo.Analises.Select(Analise),
                actions = completo.Acoes.Select(x => Acao(x, hoje)),
                reading = completo.Leitura,
            }, ctx);
        });

        // o A3 sai da mesma leitura que a tela mostra. Papel e tela dizendo números
        // diferentes parariam a reunião para descobrir em qual acreditar
        ciclos.MapGet("/{id:guid}/a3", async (Guid id, CicloDeMelhoriaService svc, TimeProvider clock,
            ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            var completo = await svc.AbrirAsync(BuildActor(p)!, id, ct);
            if (completo is null) return Error(ctx, 404, "PDCA-ERR-404", "Ciclo não encontrado.");
            var pdf = A3DoCiclo.Gerar(completo, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
            return Results.File(pdf, "application/pdf", $"{completo.Ciclo.Code}-A3.pdf");
        });

        ciclos.MapPost("/", async (CicloRequest body, CicloDeMelhoriaService svc,
            ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            var (ciclo, erro) = await svc.CriarAsync(BuildActor(p)!, body.Dados(), ct);
            return erro is not null
                ? Error(ctx, 400, erro.Code, erro.Message)
                : Results.Json(new { data = Resumo(ciclo!), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        ciclos.MapPatch("/{id:guid}", async (Guid id, CicloRequest body, CicloDeMelhoriaService svc,
            ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            var (ciclo, erro) = await svc.AtualizarAsync(BuildActor(p)!, id, body.Dados(), ct);
            return erro is not null
                ? Error(ctx, erro.Code == "PDCA-ERR-404" ? 404 : 400, erro.Code, erro.Message)
                : Ok(Resumo(ciclo!), ctx);
        });

        // encerrar é veredito, não fase: a rota própria é o que garante que ninguém feche o
        // ciclo com um clique na trilha, sem dizer se a meta foi atingida e por quê
        ciclos.MapPost("/{id:guid}/close", async (Guid id, EncerrarCicloRequest body,
            CicloDeMelhoriaService svc, TimeProvider clock, ClaimsPrincipal p, HttpContext ctx,
            CancellationToken ct) =>
        {
            var (ciclo, erro, pendentes) = await svc.EncerrarAsync(BuildActor(p)!, id,
                new PedidoDeEncerramento(body.GoalMet, body.Reason, body.ConfirmPending == true), ct);
            if (erro is null) return Ok(Resumo(ciclo!), ctx);
            if (erro.Code != "PDCA-ERR-041")
                return Error(ctx, erro.Code == "PDCA-ERR-404" ? 404 : 400, erro.Code, erro.Message);

            // a recusa vem com a lista do que sobrou: pedir confirmação sem dizer do quê
            // seria pedir uma assinatura em branco
            var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            return Results.Json(new
            {
                error = new { code = erro.Code, message = erro.Message },
                pending = pendentes.Select(a => Acao(a, hoje)),
                correlationId = CorrelationId(ctx),
            }, statusCode: 409);
        });

        // a ferramenta tem rota própria: ela é preenchida, não descrita, e salvar a folha
        // inteira a cada tecla do Ishikawa seria mandar o ciclo todo de volta
        ciclos.MapPut("/{id:guid}/tools", async (Guid id, FerramentaDoCicloRequest body,
            CicloDeMelhoriaService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var (ciclo, erro) = await svc.SalvarFerramentaAsync(id, body.Tool, body.Data, ct);
            return erro is not null
                ? Error(ctx, erro.Code == "PDCA-ERR-404" ? 404 : 400, erro.Code, erro.Message)
                : Ok(new { tools = ciclo!.Tools.OrderBy(t => t.Seq)
                    .Select(t => new { tool = t.ToolType, data = t.ToolData, seq = t.Seq }) }, ctx);
        });

        ciclos.MapDelete("/{id:guid}/tools/{tool}", async (Guid id, string tool,
            CicloDeMelhoriaService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var (ciclo, erro) = await svc.RemoverFerramentaAsync(id, tool, ct);
            return erro is not null
                ? Error(ctx, erro.Code == "PDCA-ERR-404" ? 404 : 400, erro.Code, erro.Message)
                : Ok(new { tools = ciclo!.Tools.OrderBy(t => t.Seq)
                    .Select(t => new { tool = t.ToolType, data = t.ToolData, seq = t.Seq }) }, ctx);
        });

        ciclos.MapPost("/{id:guid}/reopen", async (Guid id, ReabrirCicloRequest? body,
            CicloDeMelhoriaService svc, HttpContext ctx, CancellationToken ct) =>
        {
            var (ciclo, erro) = await svc.ReabrirAsync(id, body?.Phase, ct);
            return erro is not null
                ? Error(ctx, erro.Code == "PDCA-ERR-404" ? 404 : 400, erro.Code, erro.Message)
                : Ok(Resumo(ciclo!), ctx);
        });

        ciclos.MapPost("/{id:guid}/scope", async (Guid id, MudarEscopoRequest body,
            CicloDeMelhoriaService svc, ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            if (!CicloDeMelhoriaService.PodeMudarEscopo(RoleOf(p)))
                return Error(ctx, 403, "PDCA-ERR-901",
                    "Mudar o centro de custo do ciclo é escrever dado de centro — só quem o mantém faz isso.");
            var (ciclo, movidas, erro) = await svc.MudarEscopoAsync(id, body.Scope, body.CostCenters, ct);
            return erro is not null
                ? Error(ctx, erro.Code == "PDCA-ERR-404" ? 404 : 400, erro.Code, erro.Message)
                : Ok(new { cycle = Resumo(ciclo!), movedActions = movidas }, ctx);
        });

        ciclos.MapPut("/{id:guid}/watchers", async (Guid id, AcompanhantesRequest body,
            CicloDeMelhoriaService svc, ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            var (ciclo, erro) = await svc.DefinirAcompanhantesAsync(
                BuildActor(p)!, id, body.UserIds ?? [], ct);
            return erro is not null
                ? Error(ctx, erro.Code == "PDCA-ERR-404" ? 404 : 403, erro.Code, erro.Message)
                : Ok(new { watchers = ciclo!.Watchers.Select(w => new { userId = w.UserId, label = w.UserLabel }) }, ctx);
        });
    }
}
