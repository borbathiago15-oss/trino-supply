using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Analytics;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// As metas dos indicadores. A regra que sustenta o resto: <b>sem meta cadastrada não há
/// comparação</b> — um card que diz "85% da meta" sobre meta que ninguém definiu parece
/// conferido e não é.
/// </summary>
public class MetasDosIndicadoresTests
{
    private static readonly Actor Admin = new(Guid.NewGuid(), "Administrador", Roles.SystemAdministrator);
    private static readonly Actor Carla = new(Guid.NewGuid(), "Carla Compradora", Roles.PurchasingOfficer);

    private static MetasDosIndicadoresService Servico(out AppDbContext db)
    {
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return new MetasDosIndicadoresService(db, TimeProvider.System);
    }

    private static IndicadorComMeta I(string codigo) => MetasDosIndicadores.Do(codigo)!;

    [Fact]
    public void Maior_e_melhor_bate_a_partir_de_100_e_fica_em_atencao_a_partir_de_80()
    {
        Assert.Equal("ok", MetasDosIndicadores.Comparar(I("otif"), 90, 92, 1)!.Faixa);
        Assert.Equal("atencao", MetasDosIndicadores.Comparar(I("otif"), 90, 75, 1)!.Faixa);
        Assert.Equal("fora", MetasDosIndicadores.Comparar(I("otif"), 90, 60, 1)!.Faixa);
    }

    [Fact]
    public void Menor_e_melhor_bate_ate_a_meta_e_fica_em_atencao_ate_20_por_cento_acima()
    {
        Assert.Equal("ok", MetasDosIndicadores.Comparar(I("avgApprovalDays"), 3, 2.5m, 1)!.Faixa);
        Assert.Equal("atencao", MetasDosIndicadores.Comparar(I("avgApprovalDays"), 3, 3.5m, 1)!.Faixa);
        Assert.Equal("fora", MetasDosIndicadores.Comparar(I("avgApprovalDays"), 3, 4, 1)!.Faixa);
    }

    [Fact]
    public void A_meta_que_acumula_cresce_com_o_periodo_e_a_media_nao()
    {
        // um trimestre de saving cobra três meses de meta; o prazo médio cobra o mesmo prazo
        var tri = MetasDosIndicadores.MesesDoPeriodo(new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30));
        Assert.Equal(3.02m, tri);
        Assert.Equal(151_000m, MetasDosIndicadores.Comparar(I("saving"), 50_000, 100_000, tri)!.MetaDoPeriodo);
        Assert.Equal(3m, MetasDosIndicadores.Comparar(I("avgApprovalDays"), 3, 2, tri)!.MetaDoPeriodo);
        // meio mês cobra meia meta — a quinzena não parece abaixo da meta só por ser quinzena
        Assert.Equal(0.49m, MetasDosIndicadores.MesesDoPeriodo(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15)));
    }

    [Fact]
    public void Sem_valor_medido_nao_ha_comparacao()
    {
        // "ninguém mediu" não é "abaixo da meta"
        Assert.Null(MetasDosIndicadores.Comparar(I("otif"), 90, null, 1));
    }

    [Fact]
    public async Task Sem_meta_cadastrada_o_indicador_fica_sem_comparacao()
    {
        var svc = Servico(out _);
        var nada = await svc.CompararAsync(new Dictionary<string, decimal?> { ["saving"] = 10_000 },
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Assert.Empty(nada);

        Assert.Null((await svc.SalvarAsync(Admin, [new MetaInput("saving", 20_000)])).error);
        var com = await svc.CompararAsync(new Dictionary<string, decimal?> { ["saving"] = 10_000, ["otif"] = 80 },
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Assert.Equal("fora", Assert.Single(com).Value.Faixa);
    }

    [Fact]
    public async Task So_o_administrador_grava_e_a_meta_precisa_fazer_sentido()
    {
        var svc = Servico(out var db);
        Assert.Equal("MET-ERR-900", (await svc.SalvarAsync(Carla, [new MetaInput("saving", 1)])).error!.Code);
        Assert.Equal("MET-ERR-010", (await svc.SalvarAsync(Admin, [new MetaInput("inventado", 1)])).error!.Code);
        Assert.Equal("MET-ERR-011", (await svc.SalvarAsync(Admin, [new MetaInput("saving", 0)])).error!.Code);
        Assert.Equal("MET-ERR-012", (await svc.SalvarAsync(Admin, [new MetaInput("otif", 120)])).error!.Code);
        Assert.Empty(db.IndicatorGoals);

        await svc.SalvarAsync(Admin, [new MetaInput("otif", 90)]);
        var gravada = await db.IndicatorGoals.SingleAsync();
        Assert.Equal("Administrador", gravada.UpdatedByLabel);

        // deixar vazio apaga: o indicador volta a não ter comparação
        await svc.SalvarAsync(Admin, [new MetaInput("otif", null)]);
        Assert.Empty(db.IndicatorGoals);
    }

    [Fact]
    public async Task O_painel_compara_o_numero_do_card_com_a_meta()
    {
        var svc = Servico(out var db);
        await svc.SalvarAsync(Admin, [new MetaInput("avgApprovalDays", 3)]);
        var painel = await new AnalyticsService(db, TimeProvider.System).SupplyAsync(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null, null, null, null, null, null, null, null);
        // sem SC no período, o prazo médio não foi medido: sem comparação, mesmo com meta
        var goals = (IReadOnlyDictionary<string, ComparacaoComMeta>)painel.GetType().GetProperty("goals")!.GetValue(painel)!;
        Assert.False(goals.ContainsKey("avgApprovalDays"));
    }
}
