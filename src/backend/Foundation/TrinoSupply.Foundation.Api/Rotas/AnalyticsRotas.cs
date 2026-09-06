using System.Security.Claims;
using TrinoSupply.Foundation.Api.Analytics;
using TrinoSupply.Foundation.Api.Compliance;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Insights;
using static TrinoSupply.Foundation.Api.Rotas.Api;

namespace TrinoSupply.Foundation.Api.Rotas;

/// <summary>
/// Dashboards analíticos: suprimentos, insights, TCO, scorecard de fornecedores,
/// compliance e estoque.
///
/// Primeiro módulo a sair do Program.cs (ARQ-A). O conteúdo é o mesmo — grupo,
/// filtros e handlers na mesma ordem —, só mudou de arquivo.
/// </summary>
public static class AnalyticsRotas
{
    public static void MapAnalytics(this WebApplication app)
    {
        // SEC-A: até aqui o grupo `analytics` era o único sem filtro — os seis endpoints
        // repetiam a checagem dentro do handler, e todos acertavam. O problema era o
        // amanhã: um endpoint novo que esquecesse a linha nasceria aberto ao token do
        // Portal do Fornecedor. Os filtros abaixo são o piso, não a checagem completa —
        // cada handler continua exigindo o papel e o módulo específicos dele.
        var analytics = app.MapGroup("/api/v1/analytics").RequireAuthorization();
        analytics.AddEndpointFilter(RejectSupplierRole());
        analytics.AddEndpointFilter(RequireModules(
            AppModules.Solicitacoes, AppModules.Aprovacao, AppModules.Compras,
            AppModules.Insights, AppModules.Compliance, AppModules.Fornecedores, AppModules.Estoque));

        analytics.MapGet("/supply", async (TrinoSupply.Foundation.Api.Analytics.AnalyticsService svc,
            ClaimsPrincipal p, HttpContext ctx, TimeProvider clock,
            DateOnly? from, DateOnly? to, Guid? supplierId, Guid? buyerId, Guid? requesterId,
            string? family, string? costCenter, string? region, string? manager, string? client) =>
        {
            if (!TrinoSupply.Foundation.Api.Analytics.AnalyticsService.CanViewSupply(RoleOf(p)))
                return Error(ctx, 403, "AN-ERR-900", "Seu papel não acessa o dashboard de suprimentos.");
            var mods = ModulesOf(p);
            if (!mods.Contains(AppModules.Solicitacoes) && !mods.Contains(AppModules.Aprovacao) && !mods.Contains(AppModules.Compras))
                return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para este módulo.");
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var f = from ?? new DateOnly(today.Year, today.Month, 1).AddMonths(-11);
            var t = to ?? today;
            if (t < f) (f, t) = (t, f);
            return Ok(await svc.SupplyAsync(f, t, supplierId, buyerId, requesterId, family, costCenter, region, manager, client), ctx);
        });

        // Procurement Insights (V2-P3): achados determinísticos + visão executiva + backlog
        analytics.MapGet("/insights", async (TrinoSupply.Foundation.Api.Insights.InsightsService svc,
            ClaimsPrincipal p, HttpContext ctx, int? months) =>
        {
            if (!TrinoSupply.Foundation.Api.Insights.InsightsService.CanView(RoleOf(p)))
                return Error(ctx, 403, "INS-ERR-900", "Seu papel não acessa o painel de insights.");
            if (!ModulesOf(p).Contains(AppModules.Insights))
                return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para este módulo.");
            return Ok(await svc.ReportAsync(months ?? 6), ctx);
        });

        // TCO por produto (V2-P4): custo total de aquisição com frete/impostos rateados
        analytics.MapGet("/tco", async (TrinoSupply.Foundation.Api.Analytics.AnalyticsService svc,
            ClaimsPrincipal p, HttpContext ctx, int? months) =>
        {
            if (!TrinoSupply.Foundation.Api.Insights.InsightsService.CanView(RoleOf(p)))
                return Error(ctx, 403, "INS-ERR-900", "Seu papel não acessa o painel de TCO.");
            if (!ModulesOf(p).Contains(AppModules.Insights))
                return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para este módulo.");
            return Ok(await svc.TcoAsync(months ?? 6), ctx);
        });

        // Scorecard de fornecedores (V2-P3): classes A/B/C/D por OTIF + qualidade + competitividade
        analytics.MapGet("/supplier-scorecard", async (TrinoSupply.Foundation.Api.Analytics.AnalyticsService svc,
            ClaimsPrincipal p, HttpContext ctx, int? months) =>
        {
            if (!TrinoSupply.Foundation.Api.Analytics.AnalyticsService.CanViewSupply(RoleOf(p)))
                return Error(ctx, 403, "AN-ERR-900", "Seu papel não acessa o scorecard de fornecedores.");
            if (!ModulesOf(p).Contains(AppModules.Compras) && !ModulesOf(p).Contains(AppModules.Fornecedores))
                return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para este módulo.");
            return Ok(await svc.SupplierScorecardAsync(months ?? 6), ctx);
        });

        // Compliance Score (V2-P2 §14): derivado dos fatos do processo; mede e expõe, nunca bloqueia
        analytics.MapGet("/compliance", async (TrinoSupply.Foundation.Api.Compliance.ComplianceService svc,
            ClaimsPrincipal p, HttpContext ctx, Guid? quotationId) =>
        {
            if (!TrinoSupply.Foundation.Api.Compliance.ComplianceService.CanView(RoleOf(p)))
                return Error(ctx, 403, "CP-ERR-900", "Seu papel não acessa o painel de compliance.");
            if (!ModulesOf(p).Contains(AppModules.Compliance))
                return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para este módulo.");
            var report = await svc.ReportAsync(quotationId);
            return Ok(new
            {
                evaluated = report.Evaluated, concluded = report.Concluded,
                averageScore = report.AverageScore, fullCompliance = report.FullCompliance,
                byBuyer = report.ByBuyer.Select(g => new { label = g.Label, count = g.Count, averageScore = g.AverageScore }),
                byCostCenter = report.ByCostCenter.Select(g => new { label = g.Label, count = g.Count, averageScore = g.AverageScore }),
                items = report.Items.Select(r => new
                {
                    quotationId = r.QuotationId, number = r.Number, kind = r.Kind,
                    status = QStatusLabel(r.Status), costCenter = r.CostCenter, buyerLabel = r.BuyerLabel,
                    openedAt = r.OpenedAt, concluded = r.Concluded, score = r.Score,
                    penalties = r.Penalties.Select(pe => new { code = pe.Code, label = pe.Label, points = pe.Points, evidence = pe.Evidence }),
                }),
            }, ctx);
        });

        analytics.MapGet("/stock", async (TrinoSupply.Foundation.Api.Analytics.AnalyticsService svc,
            ClaimsPrincipal p, HttpContext ctx, Guid? locationId, string? family, int? months) =>
        {
            if (!CanViewStock(p))
                return Error(ctx, 403, "AN-ERR-900", "Seu papel não acessa o dashboard de estoque.");
            var mods = ModulesOf(p);
            if (!mods.Contains(AppModules.Estoque) && !mods.Contains(AppModules.Compras))
                return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para este módulo.");
            return Ok(await svc.StockAsync(locationId, family, months ?? 6), ctx);
        });
    }
}
