using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Acoes;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O plano de ação. A regra que mais importa aqui é a da ação <b>suspensa</b>: ela está
/// parada por decisão de quem manda, e o prazo não corre contra ela. Contá-la como atrasada
/// transformaria uma decisão da gestão em falha da equipe.
/// </summary>
public class PlanoDeAcaoTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Agora = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoje = new(2026, 9, 22);
    private static readonly Actor Carla = new(Guid.NewGuid(), "Carla Compradora", Roles.PurchasingOfficer);

    private static ActionItem Acao(string status, DateOnly? prazo) => new()
    {
        Title = "Refazer o layout da doca", Status = status, DueDate = prazo,
        ResponsibleId = Carla.Id, ResponsibleLabel = Carla.Label,
    };

    // ---- a régua do atraso ---------------------------------------------------

    [Fact]
    public void Acao_suspensa_nunca_esta_atrasada()
    {
        // é a regra: quem foi mandado parar não é cobrado pelo relógio
        var suspensa = Acao(StatusDaAcao.Suspensa, Hoje.AddDays(-10));
        Assert.False(PlanoDeAcao.Atrasada(suspensa, Hoje));
        Assert.Null(PlanoDeAcao.DiasDeAtraso(suspensa, Hoje));
        // mas continua aberta: ela não sumiu, só não corre
        Assert.True(PlanoDeAcao.Aberta(suspensa));
    }

    [Fact]
    public void Acao_pendente_com_prazo_vencido_esta_atrasada()
    {
        var atrasada = Acao(StatusDaAcao.Pendente, Hoje.AddDays(-3));
        Assert.True(PlanoDeAcao.Atrasada(atrasada, Hoje));
        Assert.Equal(3, PlanoDeAcao.DiasDeAtraso(atrasada, Hoje));
    }

    [Fact]
    public void Concluida_e_cancelada_nao_atrasam_nem_ficam_abertas()
    {
        foreach (var s in new[] { StatusDaAcao.Concluida, StatusDaAcao.Cancelada })
        {
            Assert.False(PlanoDeAcao.Atrasada(Acao(s, Hoje.AddDays(-30)), Hoje));
            Assert.False(PlanoDeAcao.Aberta(Acao(s, null)));
        }
    }

    [Fact]
    public void Prazo_de_hoje_ainda_nao_e_atraso()
    {
        // o dia do vencimento ainda é do dono da ação
        Assert.False(PlanoDeAcao.Atrasada(Acao(StatusDaAcao.Pendente, Hoje), Hoje));
    }

    [Fact]
    public void Acao_sem_prazo_nao_atrasa()
    {
        Assert.False(PlanoDeAcao.Atrasada(Acao(StatusDaAcao.Pendente, null), Hoje));
    }

    [Fact]
    public void O_progresso_segue_a_situacao_e_nao_o_numero_digitado()
    {
        // "concluída, 40%" não quer dizer nada
        var concluida = Acao(StatusDaAcao.Concluida, null); concluida.Progress = 40;
        Assert.Equal(100, PlanoDeAcao.ProgressoReal(concluida));
        var cancelada = Acao(StatusDaAcao.Cancelada, null); cancelada.Progress = 80;
        Assert.Equal(0, PlanoDeAcao.ProgressoReal(cancelada));
    }

    [Fact]
    public void O_placar_conta_a_suspensa_a_parte_e_fora_do_atraso()
    {
        var acoes = new[]
        {
            Acao(StatusDaAcao.Pendente, Hoje.AddDays(-1)),   // atrasada
            Acao(StatusDaAcao.Suspensa, Hoje.AddDays(-9)),   // vencida, mas parada por decisão
            Acao(StatusDaAcao.Concluida, Hoje.AddDays(-5)),
        };
        var placar = PlanoDeAcaoService.Placar(acoes, Hoje);
        Assert.Equal(3, placar.Total);
        Assert.Equal(1, placar.Suspensas);
        Assert.Equal(1, placar.Atrasadas);   // e não 2
    }

    // ---- gravação ------------------------------------------------------------

    private static (PlanoDeAcaoService svc, AppDbContext db, User dono) Mundo()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var dono = new User { Id = Carla.Id, Email = "c@t.dev", Name = Carla.Label, PasswordHash = "x" };
        db.Users.Add(dono);
        db.SaveChanges();
        return (new PlanoDeAcaoService(db, new FixedTimeProvider(Agora)), db, dono);
    }

    private static DadosDaAcao Dados(Guid dono, DateOnly? inicio = null, DateOnly? prazo = null) =>
        new("Refazer o layout da doca", dono, inicio, prazo, null, null, null, null, null, "CC-01");

    [Fact]
    public async Task A_acao_nasce_numerada_e_com_dono_de_verdade()
    {
        var (svc, _, dono) = Mundo();
        var (a, erro) = await svc.CriarAsync(Carla, Dados(dono.Id));
        Assert.Null(erro);
        Assert.Equal("AC-2026-000001", a!.Number);
        // o dono é chave estrangeira; o nome é só o retrato do momento
        Assert.Equal(dono.Id, a.ResponsibleId);
        Assert.Equal("Carla Compradora", a.ResponsibleLabel);
    }

    [Fact]
    public async Task Responsavel_inativo_ou_inexistente_e_recusado()
    {
        var (svc, _, _) = Mundo();
        var (_, erro) = await svc.CriarAsync(Carla, Dados(Guid.NewGuid()));
        Assert.Equal("AC-ERR-011", erro!.Code);
    }

    [Fact]
    public async Task Prazo_antes_do_inicio_e_dado_trocado_e_nao_prazo_apertado()
    {
        var (svc, _, dono) = Mundo();
        var (_, erro) = await svc.CriarAsync(Carla,
            Dados(dono.Id, inicio: Hoje.AddDays(5), prazo: Hoje.AddDays(1)));
        Assert.Equal("AC-ERR-012", erro!.Code);
    }

    [Fact]
    public async Task Suspender_e_cancelar_exigem_motivo()
    {
        var (svc, _, dono) = Mundo();
        var (a, _) = await svc.CriarAsync(Carla, Dados(dono.Id));

        foreach (var status in new[] { StatusDaAcao.Suspensa, StatusDaAcao.Cancelada })
        {
            var (_, erro) = await svc.MudarStatusAsync(a!.Id, status, null, null);
            Assert.Equal("AC-ERR-014", erro!.Code);
        }

        // com motivo, passa — e o motivo fica gravado, que é o ponto
        var (suspensa, ok) = await svc.MudarStatusAsync(
            a!.Id, StatusDaAcao.Suspensa, "Obra parada por decisão da diretoria.", null);
        Assert.Null(ok);
        Assert.Equal(StatusDaAcao.Suspensa, suspensa!.Status);
        Assert.Contains("diretoria", suspensa.StatusReason);
    }

    [Fact]
    public async Task Concluir_fecha_em_cem_por_cento_e_marca_a_data()
    {
        var (svc, _, dono) = Mundo();
        var (a, _) = await svc.CriarAsync(Carla, Dados(dono.Id));
        var (fechada, _) = await svc.MudarStatusAsync(a!.Id, StatusDaAcao.Concluida, null, 30);
        Assert.Equal(100, fechada!.Progress);
        Assert.NotNull(fechada.CompletedAt);
    }

    [Fact]
    public async Task A_numeracao_nao_repete()
    {
        var (svc, _, dono) = Mundo();
        await svc.CriarAsync(Carla, Dados(dono.Id));
        var (segunda, _) = await svc.CriarAsync(Carla, Dados(dono.Id));
        Assert.Equal("AC-2026-000002", segunda!.Number);
    }

    [Fact]
    public async Task O_filtro_de_atrasadas_usa_a_mesma_regua_da_suspensa()
    {
        var (svc, db, dono) = Mundo();
        var (venceu, _) = await svc.CriarAsync(Carla, Dados(dono.Id, prazo: Hoje.AddDays(-2)));
        var (parada, _) = await svc.CriarAsync(Carla, Dados(dono.Id, prazo: Hoje.AddDays(-2)));
        await svc.MudarStatusAsync(parada!.Id, StatusDaAcao.Suspensa, "Aguardando verba.", null);

        var pagina = await svc.ListarAsync(new FiltroDeAcoes(Atrasadas: true));

        Assert.Equal([venceu!.Number], pagina.Itens.Select(a => a.Number));
        // e o placar do topo conta a mesma coisa que a lista abre
        Assert.Equal(1, pagina.Placar.Atrasadas);
        Assert.Equal(1, pagina.Placar.Suspensas);
    }
}
