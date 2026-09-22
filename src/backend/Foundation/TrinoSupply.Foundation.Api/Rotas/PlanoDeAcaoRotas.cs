using System.Security.Claims;
using TrinoSupply.Foundation.Api.Acoes;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Procurement;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// O plano de ação: tarefa com dono, prazo e 5W2H.
///
/// <para>
/// O acesso é pelo módulo <c>PLANO_ACAO</c>, que o administrador concede no cadastro de
/// usuários — e não por papel. É uma ferramenta de trabalho de qualquer área, não de um
/// cargo: amarrá-la a papel deixaria de fora justamente quem a área escolheu para tocar.
/// </para>
/// </summary>
public static class PlanoDeAcaoRotas
{
    public static void MapPlanoDeAcao(this WebApplication app)
    {
        var acoes = app.MapGroup("/api/v1/action-items").RequireAuthorization();
        acoes.AddEndpointFilter(RejectSupplierRole());
        acoes.AddEndpointFilter(RequireModules(AppModules.PlanoAcao));

        static object Vista(ActionItem a, DateOnly hoje) => new
        {
            id = a.Id, number = a.Number, title = a.Title,
            reason = a.Reason, area = a.Area,
            responsibleId = a.ResponsibleId, responsibleLabel = a.ResponsibleLabel,
            startDate = a.StartDate, dueDate = a.DueDate,
            expectedGain = a.ExpectedGain, realizedGain = a.RealizedGain,
            expectedResult = a.ExpectedResult, kpi = a.Kpi, costCenter = a.CostCenter,
            status = a.Status, statusReason = a.StatusReason,
            // derivados: a tela não recalcula o que o servidor já sabe, senão as duas discordam
            progress = PlanoDeAcao.ProgressoReal(a),
            late = PlanoDeAcao.Atrasada(a, hoje),
            daysLate = PlanoDeAcao.DiasDeAtraso(a, hoje),
            open = PlanoDeAcao.Aberta(a),
            completedAt = a.CompletedAt, createdByLabel = a.CreatedByLabel, createdAt = a.CreatedAt,
        };

        acoes.MapGet("/", async (PlanoDeAcaoService svc, TimeProvider clock, HttpContext ctx,
            string? q, string? status, Guid? responsibleId, string? costCenter,
            bool? late, DateOnly? from, DateOnly? to, CancellationToken ct) =>
        {
            var pagina = await svc.ListarAsync(
                new FiltroDeAcoes(q, status, responsibleId, costCenter, late, from, to), ct);
            var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            return Ok(new
            {
                items = pagina.Itens.Select(a => Vista(a, hoje)),
                placar = pagina.Placar,
                filterOptions = new
                {
                    responsibles = pagina.Responsaveis.Select(r => new { id = r.Id, label = r.Label }),
                    costCenters = pagina.CentrosCusto,
                },
            }, ctx);
        });

        acoes.MapPost("/", async (CriarAcaoRequest body, PlanoDeAcaoService svc, TimeProvider clock,
            ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            if (!PlanoDeAcaoService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "AC-ERR-900", "Seu papel não mantém plano de ação.");
            var (acao, erro) = await svc.CriarAsync(BuildActor(p)!, new DadosDaAcao(
                body.Title, body.ResponsibleId, body.StartDate, body.DueDate, body.Reason,
                body.Area, body.ExpectedResult, body.Kpi, body.ExpectedGain, body.CostCenter), ct);
            if (erro is not null) return Error(ctx, 400, erro.Code, erro.Message);
            var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            return Results.Json(new { data = Vista(acao!, hoje), correlationId = CorrelationId(ctx) }, statusCode: 201);
        });

        acoes.MapPatch("/{id:guid}", async (Guid id, AtualizarAcaoRequest body, PlanoDeAcaoService svc,
            TimeProvider clock, ClaimsPrincipal p, HttpContext ctx, CancellationToken ct) =>
        {
            if (!PlanoDeAcaoService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "AC-ERR-900", "Seu papel não mantém plano de ação.");
            var (acao, erro) = await svc.AtualizarAsync(id, new DadosDaAcao(
                body.Title ?? "", body.ResponsibleId ?? Guid.Empty, body.StartDate, body.DueDate,
                body.Reason, body.Area, body.ExpectedResult, body.Kpi, body.ExpectedGain,
                body.CostCenter), body.RealizedGain, ct);
            if (erro is not null) return Error(ctx, erro.Code == "AC-ERR-404" ? 404 : 400, erro.Code, erro.Message);
            return Ok(Vista(acao!, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)), ctx);
        });

        // situação tem rota própria: suspender e cancelar exigem motivo, e misturar isso na
        // edição comum deixaria a ação parar sem ninguém assumir a decisão
        acoes.MapPost("/{id:guid}/status", async (Guid id, MudarStatusDaAcaoRequest body,
            PlanoDeAcaoService svc, TimeProvider clock, ClaimsPrincipal p, HttpContext ctx,
            CancellationToken ct) =>
        {
            if (!PlanoDeAcaoService.CanManage(RoleOf(p)))
                return Error(ctx, 403, "AC-ERR-900", "Seu papel não mantém plano de ação.");
            var (acao, erro) = await svc.MudarStatusAsync(id, body.Status, body.Reason, body.Progress, ct);
            if (erro is not null) return Error(ctx, erro.Code == "AC-ERR-404" ? 404 : 400, erro.Code, erro.Message);
            return Ok(Vista(acao!, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)), ctx);
        });
    }
}
