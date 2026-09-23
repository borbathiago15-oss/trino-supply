using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Acoes;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Melhoria;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O caminho achado → causa → contramedida.
///
/// <para>
/// O Insights já aponta o problema com a evidência junto, e parava aí: quem lia redigitava
/// na mão o que a tela já dizia. O que este teste guarda é a regra que impede o remédio de
/// virar doença — <b>clicar duas vezes não abre dois ciclos</b>.
/// </para>
/// </summary>
public class CausaDoAchadoTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Agora = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly Actor Carla = new(Guid.NewGuid(), "Carla Compradora", Roles.PurchasingOfficer);

    private static AppDbContext Banco() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CausaDoAchadoService Servico(AppDbContext db) =>
        new(db, new FixedTimeProvider(Agora));

    private static AchadoATratar Achado(bool plano = false) => new(
        "INS-01", "Sobrepreço em LUVA NITRÍLICA TAM. M",
        "Pago R$ 12,40 contra R$ 8,90 no último pedido, no mesmo centro.",
        "Renegocie com o fornecedor antes da próxima cotação.",
        "BAH-001", plano);

    [Fact]
    public async Task O_achado_abre_o_ciclo_com_o_problema_e_a_evidencia_ja_escritos()
    {
        var db = Banco();
        var (aberta, erro) = await Servico(db).AbrirAsync(Carla, Achado());

        Assert.Null(erro);
        Assert.False(aberta!.JaExistia);
        Assert.Equal("Sobrepreço em LUVA NITRÍLICA TAM. M", aberta.Ciclo.Problem);
        Assert.Contains("R$ 8,90", aberta.Ciclo.CurrentSituation);
        Assert.Equal(FaseDoCiclo.Plan, aberta.Ciclo.Phase);
        Assert.Contains("INS-01", aberta.Ciclo.OriginLabel);
    }

    [Fact]
    public async Task Os_cinco_porques_ja_comecam_com_o_problema_escrito()
    {
        // deixar a folha em branco devolveria para quem lê o trabalho que a tela já fez
        var db = Banco();
        var (aberta, _) = await Servico(db).AbrirAsync(Carla, Achado());

        var ferramenta = Assert.Single(aberta!.Ciclo.Tools);
        Assert.Equal(FerramentaDeCausa.CincoPorques, ferramenta.ToolType);
        var analise = FerramentaDeCausa.Normalizar(ferramenta.ToolType, ferramenta.ToolData)!;
        Assert.Equal("Sobrepreço em LUVA NITRÍLICA TAM. M", analise.Problema);
        Assert.Single(analise.Degraus);
    }

    [Fact]
    public async Task Clicar_duas_vezes_nao_abre_dois_ciclos()
    {
        // senão a lista encheria de ciclos gêmeos, cada um com um pedaço da análise
        var db = Banco();
        var svc = Servico(db);
        var (primeira, _) = await svc.AbrirAsync(Carla, Achado());
        var (segunda, _) = await svc.AbrirAsync(Carla, Achado());

        Assert.False(primeira!.JaExistia);
        Assert.True(segunda!.JaExistia);
        Assert.Equal(primeira.Ciclo.Id, segunda.Ciclo.Id);
        Assert.Equal(1, await db.ImprovementCycles.CountAsync());
    }

    [Fact]
    public async Task O_mesmo_achado_com_o_texto_mexido_continua_sendo_o_mesmo_problema()
    {
        // a chave normaliza acento, caixa e pontuação: um mês para o outro o texto muda,
        // o problema não
        Assert.Equal(
            CausaDoAchadoService.ChaveDoAchado("INS-01", "Sobrepreço em LUVA NITRÍLICA TAM. M"),
            CausaDoAchadoService.ChaveDoAchado("ins-01", "sobrepreco em luva nitrilica tam m"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Achados_diferentes_abrem_ciclos_diferentes()
    {
        var db = Banco();
        var svc = Servico(db);
        await svc.AbrirAsync(Carla, Achado());
        await svc.AbrirAsync(Carla, Achado() with { Code = "INS-05", Title = "Compra fechada sem O.C. do ERP" });

        Assert.Equal(2, await db.ImprovementCycles.CountAsync());
    }

    [Fact]
    public async Task O_plano_da_contramedida_nasce_apontando_para_o_ciclo()
    {
        var db = Banco();
        var (aberta, _) = await Servico(db).AbrirAsync(Carla, Achado(plano: true));

        Assert.NotNull(aberta!.Plano);
        Assert.Equal(aberta.Ciclo.Id, aberta.Plano!.CycleId);
        Assert.StartsWith("Contramedida:", aberta.Plano.Title);
        Assert.Equal("Sobrepreço em LUVA NITRÍLICA TAM. M", aberta.Plano.Problem);
        // a providência que o achado sugere vira o porquê do plano
        Assert.Contains("Renegocie", aberta.Plano.BusinessReason);
        Assert.Equal("BAH-001", aberta.Plano.CostCenter);
        // e quem abriu responde por ele até alguém dizer outra coisa
        Assert.Equal(Carla.Id, (await db.ActionPlanResponsibles.SingleAsync()).UserId);
    }

    [Fact]
    public async Task Sem_pedir_o_plano_nenhum_plano_nasce()
    {
        var db = Banco();
        var (aberta, _) = await Servico(db).AbrirAsync(Carla, Achado());
        Assert.Null(aberta!.Plano);
        Assert.Equal(0, await db.ActionPlans.CountAsync());
    }

    [Fact]
    public async Task Achado_sem_codigo_ou_sem_titulo_nao_vira_ciclo()
    {
        var db = Banco();
        var (_, semCodigo) = await Servico(db).AbrirAsync(Carla, Achado() with { Code = " " });
        Assert.Equal("PDCA-ERR-017", semCodigo!.Code);

        var (_, semTitulo) = await Servico(db).AbrirAsync(Carla, Achado() with { Title = "" });
        Assert.Equal("PDCA-ERR-017", semTitulo!.Code);
    }

    [Fact]
    public async Task Titulo_curto_demais_ganha_um_verbo_para_nao_virar_rotulo()
    {
        var db = Banco();
        var (aberta, erro) = await Servico(db).AbrirAsync(Carla, Achado() with { Title = "Avarias" });
        Assert.Null(erro);
        Assert.Equal("Tratar Avarias", aberta!.Ciclo.Title);
    }

    [Fact]
    public async Task Achado_sem_centro_abre_ciclo_de_gestao()
    {
        var db = Banco();
        var (aberta, _) = await Servico(db).AbrirAsync(Carla, Achado() with { CostCenter = null });
        Assert.Equal(EscopoDoCiclo.Gestao, aberta!.Ciclo.Scope);
        Assert.Empty(aberta.Ciclo.CostCenters);
    }

    [Fact]
    public async Task Quem_abre_o_ciclo_do_achado_o_enxerga()
    {
        // é o dono: sem isso ele abriria um ciclo que não aparece na lista dele
        var db = Banco();
        var eu = new User { Id = Carla.Id, Email = "c@t.dev", Name = Carla.Label, PasswordHash = "x" };
        db.Users.Add(eu);
        await db.SaveChangesAsync();

        var (aberta, _) = await Servico(db).AbrirAsync(Carla, Achado());
        var visiveis = await new CicloDeMelhoriaService(db, new FixedTimeProvider(Agora)).VisiveisAsync(eu);
        Assert.True(await visiveis.AnyAsync(c => c.Id == aberta!.Ciclo.Id));
    }
}
