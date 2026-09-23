using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Acoes;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Insights;
using TrinoSupply.Foundation.Api.Melhoria;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O gatilho: o achado que <b>não passa</b> abre o plano sozinho.
///
/// <para>
/// A regra que mais importa aqui é a que separa <b>dias</b> de <b>visitas</b>: contar chamadas
/// faria dez atualizações da tela na mesma tarde parecerem um achado recorrente, e o gatilho
/// abriria plano no primeiro F5.
/// </para>
/// </summary>
public class GatilhoDePlanoTests
{
    private sealed class RelogioMovel(DateTimeOffset inicio) : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = inicio;
        public override DateTimeOffset GetUtcNow() => Agora;
    }

    private static readonly DateTimeOffset Inicio = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private static AppDbContext Banco() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static (GatilhoDePlanoService svc, RelogioMovel relogio) Servico(AppDbContext db)
    {
        var relogio = new RelogioMovel(Inicio);
        return (new GatilhoDePlanoService(db, new AvisoDoUsuarioService(db, relogio), relogio), relogio);
    }

    private static Insight Achado(string severidade = "alta") => new(
        "INS-01", "sobrepreco", severidade,
        "Sobrepreço em LUVA NITRÍLICA TAM. M",
        "Pago R$ 12,40 contra R$ 8,90 no último pedido.",
        "Renegocie com o fornecedor antes da próxima cotação.", "quotations");

    /// <summary>Roda o gatilho em N dias seguidos, com M leituras por dia.</summary>
    private static async Task<List<ActionPlan>> DiasAsync(
        AppDbContext db, GatilhoDePlanoService svc, RelogioMovel relogio,
        int dias, Insight achado, int leiturasPorDia = 1)
    {
        var abertos = new List<ActionPlan>();
        for (var d = 0; d < dias; d++)
        {
            relogio.Agora = Inicio.AddDays(d);
            for (var i = 0; i < leiturasPorDia; i++)
                abertos.AddRange(await svc.AvaliarAsync([achado]));
        }
        return abertos;
    }

    [Fact]
    public async Task O_achado_de_hoje_nao_abre_plano_nenhum()
    {
        // um dia não é recorrência: pode ser o dado de hoje
        var db = Banco();
        var (svc, _) = Servico(db);
        Assert.Empty(await svc.AvaliarAsync([Achado()]));
        Assert.Equal(0, await db.ActionPlans.CountAsync());
        Assert.Equal(1, (await db.InsightSightings.SingleAsync()).Days);
    }

    [Fact]
    public async Task Tres_dias_diferentes_abrem_o_plano()
    {
        var db = Banco();
        var (svc, relogio) = Servico(db);
        var abertos = await DiasAsync(db, svc, relogio, GatilhoDePlanoService.DiasParaOGatilho, Achado());

        var plano = Assert.Single(abertos);
        Assert.StartsWith("Contramedida:", plano.Title);
        Assert.Equal("Sobrepreço em LUVA NITRÍLICA TAM. M", plano.Problem);
        Assert.Contains("Renegocie", plano.BusinessReason);
        Assert.Equal("ALTA", plano.Priority);
        Assert.NotNull(plano.AutoKey);
    }

    [Fact]
    public async Task Dez_visitas_no_mesmo_dia_nao_valem_tres_dias()
    {
        // era assim que o gatilho abriria plano no primeiro F5
        var db = Banco();
        var (svc, relogio) = Servico(db);
        var abertos = await DiasAsync(db, svc, relogio, dias: 1, Achado(), leiturasPorDia: 10);

        Assert.Empty(abertos);
        Assert.Equal(1, (await db.InsightSightings.SingleAsync()).Days);
    }

    [Fact]
    public async Task Depois_de_abrir_o_plano_ele_nao_abre_um_segundo()
    {
        var db = Banco();
        var (svc, relogio) = Servico(db);
        await DiasAsync(db, svc, relogio, dias: 6, Achado());

        Assert.Equal(1, await db.ActionPlans.CountAsync());
    }

    [Fact]
    public async Task Achado_que_nao_e_de_severidade_alta_nao_dispara()
    {
        // gatilho que dispara com tudo enche a lista de planos que ninguém pediu, e a
        // primeira coisa que se aprende é a ignorá-la
        var db = Banco();
        var (svc, relogio) = Servico(db);
        var abertos = await DiasAsync(db, svc, relogio, dias: 5, Achado("media"));

        Assert.Empty(abertos);
        Assert.Equal(5, (await db.InsightSightings.SingleAsync()).Days);
    }

    [Fact]
    public async Task O_achado_que_piora_para_alta_dispara_sem_recomecar_a_contagem()
    {
        // o problema é o mesmo; o que mudou foi a gravidade, e ela não apaga o histórico
        var db = Banco();
        var (svc, relogio) = Servico(db);
        await DiasAsync(db, svc, relogio, dias: 4, Achado("media"));

        relogio.Agora = Inicio.AddDays(4);
        var abertos = await svc.AvaliarAsync([Achado("alta")]);
        Assert.Single(abertos);
    }

    [Fact]
    public async Task O_plano_do_gatilho_nasce_sem_responsavel_e_os_gestores_sao_avisados()
    {
        // pendurá-lo em quem por acaso abriu a tela seria dar trabalho a um sorteado;
        // quem recebe o recado é quem distribui
        var db = Banco();
        db.Users.Add(new User
        {
            Email = "gustavo@t.com", Name = "Gustavo", Role = Roles.SupplyManager, PasswordHash = "x",
        });
        await db.SaveChangesAsync();

        var (svc, relogio) = Servico(db);
        await DiasAsync(db, svc, relogio, GatilhoDePlanoService.DiasParaOGatilho, Achado());

        Assert.Equal(0, await db.ActionPlanResponsibles.CountAsync());
        var aviso = await db.UserNotices.SingleAsync();
        Assert.Equal(AvisoDoCicloKinds.PlanoPorGatilho, aviso.Kind);
        Assert.Contains("sem responsável", aviso.Body);
    }

    [Fact]
    public async Task O_aviso_do_gatilho_nao_nasce_duas_vezes()
    {
        var db = Banco();
        db.Users.Add(new User
        {
            Email = "gustavo@t.com", Name = "Gustavo", Role = Roles.SupplyManager, PasswordHash = "x",
        });
        await db.SaveChangesAsync();

        var (svc, relogio) = Servico(db);
        await DiasAsync(db, svc, relogio, dias: 8, Achado());

        Assert.Equal(1, await db.UserNotices.CountAsync());
    }

    [Fact]
    public async Task Achados_diferentes_contam_separado()
    {
        var db = Banco();
        var (svc, relogio) = Servico(db);
        var outro = Achado() with { Code = "INS-05", Title = "Compra fechada sem O.C. do ERP" };

        for (var d = 0; d < 3; d++)
        {
            relogio.Agora = Inicio.AddDays(d);
            await svc.AvaliarAsync(d == 0 ? [Achado(), outro] : [Achado()]);
        }

        var vistos = await db.InsightSightings.OrderBy(s => s.Code).ToListAsync();
        Assert.Equal([3, 1], vistos.Select(v => v.Days));
        Assert.Equal(1, await db.ActionPlans.CountAsync());
    }

    [Fact]
    public async Task Sem_achado_nenhum_o_gatilho_nao_grava_nada()
    {
        var db = Banco();
        var (svc, _) = Servico(db);
        Assert.Empty(await svc.AvaliarAsync([]));
        Assert.Equal(0, await db.InsightSightings.CountAsync());
    }

    [Fact]
    public async Task O_plano_do_gatilho_aparece_na_lista_como_qualquer_outro()
    {
        var db = Banco();
        var (svc, relogio) = Servico(db);
        await DiasAsync(db, svc, relogio, GatilhoDePlanoService.DiasParaOGatilho, Achado());

        var lista = await new PlanoDeAcaoService(db, relogio).ListarAsync(new FiltroDePlanos());
        var plano = Assert.Single(lista.Itens);
        Assert.Equal("Gatilho automático", plano.CreatedByLabel);
        Assert.Equal(SituacaoDoPlano.Pendente, PlanoDoPlano.Situacao(plano, DateOnly.FromDateTime(relogio.Agora.UtcDateTime)));
    }
}
