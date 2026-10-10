using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TrinoSupply.Foundation.Api.Analytics;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Compliance;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Procurement;
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
        // SEC-B: seis consultas pesadas, sem limite nenhum até aqui. A cota é por
        // usuário e generosa — o painel inteiro cabe numa fração dela.
        analytics.RequireRateLimiting("relatorio");
        analytics.AddEndpointFilter(RejectSupplierRole());
        analytics.AddEndpointFilter(RequireModules(
            AppModules.Solicitacoes, AppModules.Aprovacao, AppModules.Compras,
            AppModules.Insights, AppModules.Compliance, AppModules.Fornecedores, AppModules.Estoque));

        analytics.MapGet("/supply", async (TrinoSupply.Foundation.Api.Analytics.AnalyticsService svc,
            ClaimsPrincipal p, HttpContext ctx, TimeProvider clock,
            DateOnly? from, DateOnly? to, Guid? supplierId, Guid? buyerId, Guid? requesterId,
            string? family, string? costCenter, string? region, string? manager, string? client,
            string? company, string? category, string? priority) =>
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
            return Ok(await svc.SupplyAsync(f, t, supplierId, buyerId, requesterId, family, costCenter, region, manager, client,
                company, category, priority), ctx);
        });

        // a solicitação de material no Dashboard e na Visão da diretoria, com filtros próprios
        // (período, centro, família, produto): o outro cano da casa, ao lado da compra
        analytics.MapGet("/material", async (AnalyticsDeMaterialService svc, ClaimsPrincipal p, HttpContext ctx,
            TimeProvider clock, DateOnly? from, DateOnly? to, string? costCenter, string? family, string? product) =>
        {
            if (!AnalyticsDeMaterialService.CanView(RoleOf(p)))
                return Error(ctx, 403, "AN-ERR-901", "Seu papel não acessa a análise de solicitações de material.");
            var mods = ModulesOf(p);
            if (!mods.Contains(AppModules.Material) && !mods.Contains(AppModules.Estoque)
                && !mods.Contains(AppModules.Aprovacao) && !mods.Contains(AppModules.Compras))
                return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para este módulo.");
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var f = from ?? new DateOnly(today.Year, today.Month, 1);
            var t = to ?? today;
            return Ok(await svc.MaterialAsync(f, t, costCenter, family, product), ctx);
        });

        // ---- o relatório de solicitações de material -----------------------------------
        //
        // Os mesmos números do bloco do Dashboard, mais a lista completa do período com valores —
        // em JSON para a tela, em PDF para a reunião e em planilha para quem quer filtrar e somar.
        // A mesma autorização do bloco: quem lê a compra lê o material, e o almoxarife também.
        static IResult? GuardaDoMaterial(ClaimsPrincipal p, HttpContext ctx)
        {
            if (!AnalyticsDeMaterialService.CanView(RoleOf(p)))
                return Error(ctx, 403, "AN-ERR-901", "Seu papel não acessa o relatório de solicitações de material.");
            var mods = ModulesOf(p);
            if (!mods.Contains(AppModules.Material) && !mods.Contains(AppModules.Estoque)
                && !mods.Contains(AppModules.Aprovacao) && !mods.Contains(AppModules.Compras))
                return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para este módulo.");
            return null;
        }
        static (DateOnly De, DateOnly Ate) JanelaDoMaterial(TimeProvider clock, DateOnly? from, DateOnly? to)
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            return (from ?? new DateOnly(today.Year, today.Month, 1), to ?? today);
        }
        static RecorteDoRelatorioDeMaterial RecorteDoMaterial(RelatorioDeMaterial r, string? costCenter, string? family, string? product) =>
            new(string.IsNullOrWhiteSpace(costCenter) ? null
                    : r.FilterOptions.CostCenters.FirstOrDefault(c => string.Equals(c.Code, costCenter.Trim(), StringComparison.OrdinalIgnoreCase)) is { } cc
                        ? $"{cc.Code} — {cc.Name}" : costCenter.Trim(),
                string.IsNullOrWhiteSpace(family) ? null : family.Trim(),
                string.IsNullOrWhiteSpace(product) ? null : product.Trim(),
                r.RequesterLabel);

        analytics.MapGet("/material/report", async (AnalyticsDeMaterialService svc, ClaimsPrincipal p, HttpContext ctx,
            TimeProvider clock, DateOnly? from, DateOnly? to, string? costCenter, string? family, string? product,
            Guid? requesterId, CancellationToken ct) =>
        {
            if (GuardaDoMaterial(p, ctx) is { } recusa) return recusa;
            var (f, t) = JanelaDoMaterial(clock, from, to);
            return Ok(await svc.MaterialAsync(f, t, costCenter, family, product, requesterId: requesterId, comLista: true, ct: ct), ctx);
        });

        analytics.MapGet("/material/report/pdf", async (AnalyticsDeMaterialService svc, AppDbContext db,
            ClaimsPrincipal p, HttpContext ctx, TimeProvider clock, IWebHostEnvironment env,
            DateOnly? from, DateOnly? to, string? costCenter, string? family, string? product, Guid? requesterId,
            CancellationToken ct) =>
        {
            if (GuardaDoMaterial(p, ctx) is { } recusa) return recusa;
            var (f, t) = JanelaDoMaterial(clock, from, to);
            var r = await svc.MaterialAsync(f, t, costCenter, family, product, requesterId: requesterId, comLista: true, ct: ct);
            var perfil = await db.CompanyProfiles.FirstOrDefaultAsync(ct);
            var logoPath = Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"),
                "assets", "brand", "trino-supply-logo.png");
            var logo = File.Exists(logoPath) ? await File.ReadAllBytesAsync(logoPath, ct) : null;
            var pdf = RelatorioDeMaterialPdf.Generate(r, RecorteDoMaterial(r, costCenter, family, product), perfil,
                p.FindFirstValue("name") ?? "Sistema", clock.GetUtcNow(), logo);
            // leitura do momento, como o PDF do executivo: não vai para stored_document
            return Results.File(pdf, "application/pdf", $"relatorio-material-{r.From:yyyy-MM-dd}_a_{r.To:yyyy-MM-dd}.pdf");
        });

        analytics.MapGet("/material/report/xlsx", async (AnalyticsDeMaterialService svc, ClaimsPrincipal p, HttpContext ctx,
            TimeProvider clock, DateOnly? from, DateOnly? to, string? costCenter, string? family, string? product,
            Guid? requesterId, CancellationToken ct) =>
        {
            if (GuardaDoMaterial(p, ctx) is { } recusa) return recusa;
            var (f, t) = JanelaDoMaterial(clock, from, to);
            var r = await svc.MaterialAsync(f, t, costCenter, family, product, requesterId: requesterId, comLista: true, ct: ct);
            return Results.File(RelatorioDeMaterialPlanilha.Gerar(r),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"solicitacoes-de-material-{r.From:yyyy-MM-dd}_a_{r.To:yyyy-MM-dd}.xlsx");
        });

        // Procurement Insights (V2-P3): achados determinísticos + visão executiva + backlog
        analytics.MapGet("/insights", async (TrinoSupply.Foundation.Api.Insights.InsightsService svc,
            TrinoSupply.Foundation.Api.Melhoria.GatilhoDePlanoService gatilho,
            ClaimsPrincipal p, HttpContext ctx, int? months, CancellationToken ct) =>
        {
            if (!TrinoSupply.Foundation.Api.Insights.InsightsService.CanView(RoleOf(p)))
                return Error(ctx, 403, "INS-ERR-900", "Seu papel não acessa o painel de insights.");
            if (!ModulesOf(p).Contains(AppModules.Insights))
                return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para este módulo.");

            var (relatorio, achados) = await svc.ReportAsync(months ?? 6, ct);
            // o gatilho é avaliado aqui, na leitura: o projeto não tem agendador, e um
            // relógio de servidor entregaria o mesmo recado com uma peça a mais que pode
            // falhar em silêncio. Quem conta é o dia, não a visita — ver GatilhoDePlanoService
            await gatilho.AvaliarAsync(achados, ct);
            return Ok(relatorio, ctx);
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

        // De quem a empresa depende, por produto. Fica no painel de compliance porque é
        // risco, não desempenho: a lista só traz o que merece ação, e cada linha diz qual.
        analytics.MapGet("/supplier-concentration", async (HistoricoDePrecoService svc,
            ClaimsPrincipal p, HttpContext ctx) =>
        {
            if (!TrinoSupply.Foundation.Api.Compliance.ComplianceService.CanView(RoleOf(p)))
                return Error(ctx, 403, "CP-ERR-900", "Seu papel não acessa o painel de compliance.");
            if (!ModulesOf(p).Contains(AppModules.Compliance))
                return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para este módulo.");
            var riscos = await svc.ConcentracaoAsync();
            return Ok(new
            {
                minPurchases = HistoricoDePrecoService.ComprasMinimasParaRisco,
                items = riscos.Select(r => new
                {
                    catalogItemId = r.CatalogItemId, description = r.Description,
                    total = r.Total, purchases = r.Compras, suppliers = r.Fornecedores,
                    level = r.Nivel, recommendation = r.Recomendacao,
                    topSupplierId = r.Maior.SupplierId, topSupplier = r.Maior.SupplierName,
                    topShare = r.Maior.Pct, topValue = r.Maior.Valor,
                }),
            }, ctx);
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

        // Relatório executivo (diretoria): os seis blocos sobre um mesmo recorte, em JSON
        // para a tela e em PDF para a reunião. O período padrão é o mês corrente — quem
        // abre a tela quer ver o mês, não doze meses somados.
        static FiltroRelatorio Recorte(TimeProvider clock, DateOnly? de, DateOnly? ate,
            string? empresa, string? centroCusto, Guid? compradorId)
        {
            // "todos" chega como string vazia do <select> da tela: vazio é ausência de
            // filtro, não um centro de custo chamado "".
            static string? Preenchido(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            var hoje = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var f = de ?? new DateOnly(hoje.Year, hoje.Month, 1);
            var t = ate ?? hoje;
            if (t < f) (f, t) = (t, f);
            return new FiltroRelatorio(f, t, Preenchido(empresa), Preenchido(centroCusto), compradorId);
        }

        analytics.MapGet("/report", async (RelatorioExecutivoService svc, MetasDosIndicadoresService metas,
            ClaimsPrincipal p, HttpContext ctx,
            TimeProvider clock, DateOnly? from, DateOnly? to, string? company, string? costCenter,
            Guid? buyerId, CancellationToken ct) =>
        {
            if (!RelatorioExecutivoService.CanView(RoleOf(p)))
                return Error(ctx, 403, "AN-ERR-900", "Seu papel não acessa o relatório executivo.");
            if (!ModulesOf(p).Contains(AppModules.Compras) && !ModulesOf(p).Contains(AppModules.Insights))
                return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para este módulo.");
            var rel = await svc.GerarAsync(Recorte(clock, from, to, company, costCenter, buyerId), ct);
            // a diretoria compara com as mesmas metas do painel, pela mesma régua
            var goals = await metas.CompararAsync(new Dictionary<string, decimal?>
            {
                ["poTotalValue"] = rel.Kpis.Spend,
                ["saving"] = rel.Kpis.SavingPercent != null ? rel.Kpis.SavingTotal : null,
                ["otif"] = (decimal?)rel.Kpis.OtifPercent,
            }, rel.From, rel.To, ct);
            return Ok(rel with { Goals = goals }, ctx);
        });

        analytics.MapGet("/report/pdf", async (RelatorioExecutivoService svc, AppDbContext db,
            ClaimsPrincipal p, HttpContext ctx, TimeProvider clock, IWebHostEnvironment env,
            DateOnly? from, DateOnly? to, string? company, string? costCenter, Guid? buyerId,
            CancellationToken ct) =>
        {
            if (!RelatorioExecutivoService.CanView(RoleOf(p)))
                return Error(ctx, 403, "AN-ERR-900", "Seu papel não acessa o relatório executivo.");
            if (!ModulesOf(p).Contains(AppModules.Compras) && !ModulesOf(p).Contains(AppModules.Insights))
                return Error(ctx, 403, "IAM-ERR-018", "Seu usuário não tem autorização para este módulo.");
            var relatorio = await svc.GerarAsync(Recorte(clock, from, to, company, costCenter, buyerId), ct);
            var perfil = await db.CompanyProfiles.FirstOrDefaultAsync(ct);
            // o logotipo oficial sai do mesmo lugar que a tela o serve; sem o arquivo, a marca vai em texto
            var logoPath = Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"),
                "assets", "brand", "trino-supply-logo.png");
            var logo = File.Exists(logoPath) ? await File.ReadAllBytesAsync(logoPath, ct) : null;
            var pdf = RelatorioExecutivoPdf.Generate(relatorio, perfil, p.FindFirstValue("name") ?? "Sistema", logo);
            // sem gravar em stored_document, ao contrário do PDF da O.C.: a O.C. é
            // documento do processo e fica no histórico; relatório é uma leitura do
            // momento, e guardar uma cópia por clique só engordaria o banco.
            return Results.File(pdf, "application/pdf",
                $"relatorio-compras-{relatorio.From:yyyy-MM-dd}_a_{relatorio.To:yyyy-MM-dd}.pdf");
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
