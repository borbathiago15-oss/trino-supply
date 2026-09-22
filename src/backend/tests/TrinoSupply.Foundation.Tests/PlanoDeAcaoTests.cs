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
        var placar = PlanoDeAcao.Placar(acoes, Hoje);
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

    private static DadosDoPlano DadosDoPlano(
        string titulo = "Reduzir avarias na doca 2", IReadOnlyList<Guid>? donos = null) =>
        new(titulo, null, "CC-01", ["Operações"], null, "ALTA", null, null, null,
            "Avarias sobem desde julho", "Custa 40 mil por mês", "Melhoria Contínua",
            null, null, null, null, null, null, "ALTA", "MEDIA",
            null, null, null, null, null, donos);

    private static async Task<ActionPlan> PlanoAsync(PlanoDeAcaoService svc, Guid? dono = null)
    {
        var (plano, erro) = await svc.CriarPlanoAsync(Carla,
            DadosDoPlano(donos: dono is { } d ? [d] : null));
        Assert.Null(erro);
        return plano!;
    }

    private static DadosDaAcao Dados(Guid dono, DateOnly? inicio = null, DateOnly? prazo = null) =>
        new("Refazer o layout da doca", dono, inicio, prazo, null, null, null, null, null, "CC-01");

    // ---- o plano -------------------------------------------------------------

    [Fact]
    public async Task O_plano_nasce_numerado_com_o_porque_e_os_responsaveis()
    {
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);

        Assert.Equal("AP-2026-001", plano.Code);
        Assert.Equal("Avarias sobem desde julho", plano.Problem);
        Assert.Equal("Custa 40 mil por mês", plano.BusinessReason);
        Assert.Equal("Operações", plano.Areas);
        // vários responsáveis: um plano de verdade atravessa áreas
        Assert.Equal([dono.Id], plano.Responsibles.Select(r => r.UserId));
    }

    [Fact]
    public async Task Titulo_curto_demais_e_recusado()
    {
        var (svc, _, _) = Mundo();
        var (_, erro) = await svc.CriarPlanoAsync(Carla, DadosDoPlano("Doca"));
        Assert.Equal("AP-ERR-010", erro!.Code);
    }

    [Fact]
    public async Task Responsavel_inativo_no_plano_e_recusado()
    {
        var (svc, _, _) = Mundo();
        var (_, erro) = await svc.CriarPlanoAsync(Carla, DadosDoPlano(donos: [Guid.NewGuid()]));
        Assert.Equal("AP-ERR-011", erro!.Code);
    }

    [Fact]
    public async Task A_situacao_do_plano_e_derivada_e_cancelado_vence_atrasado()
    {
        // fosse outra ordem, um plano cancelado apareceria como atrasado e a lista cobraria
        // trabalho que ninguém mais vai fazer
        var plano = new ActionPlan { DueDate = Hoje.AddDays(-5), Completion = 30 };
        Assert.Equal(SituacaoDoPlano.Atrasado, PlanoDoPlano.Situacao(plano, Hoje));

        plano.Cancelled = true;
        Assert.Equal(SituacaoDoPlano.Cancelado, PlanoDoPlano.Situacao(plano, Hoje));

        plano.Cancelled = false; plano.Completion = 100;
        Assert.Equal(SituacaoDoPlano.Concluido, PlanoDoPlano.Situacao(plano, Hoje));

        plano.Completion = 0; plano.DueDate = Hoje.AddDays(5);
        Assert.Equal(SituacaoDoPlano.Pendente, PlanoDoPlano.Situacao(plano, Hoje));
    }

    [Fact]
    public async Task O_progresso_dos_itens_e_a_media_deles_e_nao_o_numero_digitado()
    {
        var (svc, db, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);
        var (a1, _) = await svc.CriarAcaoAsync(Carla, plano.Id, Dados(dono.Id));
        var (a2, _) = await svc.CriarAcaoAsync(Carla, plano.Id, Dados(dono.Id));
        await svc.MudarStatusAsync(a1!.Id, StatusDaAcao.Concluida, null, null);
        await svc.MudarStatusAsync(a2!.Id, StatusDaAcao.EmAndamento, null, 40);

        var lido = await svc.AbrirAsync(plano.Id);
        Assert.Equal(70, PlanoDoPlano.ProgressoDosItens(lido!));   // (100 + 40) / 2
        // e o digitado continua existindo: são perguntas diferentes, e divergir é informação
        Assert.Equal(0, lido!.Completion);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Plano_sem_item_cai_no_progresso_digitado()
    {
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);
        plano.Completion = 55;
        Assert.Equal(55, PlanoDoPlano.ProgressoDosItens(plano));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task O_saving_do_plano_soma_o_dele_e_o_dos_itens()
    {
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);
        await svc.AtualizarPlanoAsync(plano.Id, DadosDoPlano() with
        {
            SavingExpected = 10_000m, SavingRealized = 4_000m, InvestmentActual = 2_000m,
        });
        await svc.CriarAcaoAsync(Carla, plano.Id, Dados(dono.Id) with { ExpectedGain = 5_000m });

        var lido = await svc.AbrirAsync(plano.Id);
        Assert.Equal(15_000m, PlanoDoPlano.SavingEsperado(lido!));
        Assert.Equal(4_000m, PlanoDoPlano.SavingRealizado(lido!));
        // ROI = (4000 - 2000) / 2000 = 100%
        Assert.Equal(100m, PlanoDoPlano.Roi(lido!));
    }

    [Fact]
    public async Task Sem_investimento_o_ROI_e_nulo_e_nao_zero()
    {
        // dividir por zero daria um número que parece ótimo e não quer dizer nada
        var plano = new ActionPlan { SavingRealized = 9_000m };
        Assert.Null(PlanoDoPlano.Roi(plano));
        await Task.CompletedTask;
    }

    // ---- encerrar e reabrir --------------------------------------------------

    [Fact]
    public async Task Encerrar_registra_quem_decidiu_e_a_evidencia()
    {
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);

        var (fechado, erro) = await svc.EncerrarAsync(Carla, plano.Id, "Procedimento afixado na doca.");
        Assert.Null(erro);
        Assert.Equal(VidaDoPlano.Encerrado, fechado!.Life);
        Assert.Equal(Carla.Id, fechado.ClosedById);
        Assert.NotNull(fechado.ClosedAt);
        Assert.Contains("afixado", fechado.EvidenceNote);
    }

    [Fact]
    public async Task Progresso_nao_encerra_plano_nenhum()
    {
        // um plano a 100% continua ativo até alguém dizer que acabou
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);
        await svc.AtualizarPlanoAsync(plano.Id, DadosDoPlano() with { Completion = 100 });

        var lido = await svc.AbrirAsync(plano.Id);
        Assert.Equal(VidaDoPlano.Ativo, lido!.Life);
        Assert.Equal(SituacaoDoPlano.Concluido, PlanoDoPlano.Situacao(lido, Hoje));
    }

    [Fact]
    public async Task Plano_encerrado_nao_se_edita_nem_recebe_acao()
    {
        // deixá-lo aceitar edição depois faria o registro do encerramento mentir sobre o
        // que estava fechado
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);
        await svc.EncerrarAsync(Carla, plano.Id, null);

        var (_, erroEdicao) = await svc.AtualizarPlanoAsync(plano.Id, DadosDoPlano("Outro título aqui"));
        Assert.Equal("AP-ERR-020", erroEdicao!.Code);

        var (_, erroAcao) = await svc.CriarAcaoAsync(Carla, plano.Id, Dados(dono.Id));
        Assert.Equal("AP-ERR-020", erroAcao!.Code);
    }

    [Fact]
    public async Task Reabrir_apaga_quem_encerrou()
    {
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);
        await svc.EncerrarAsync(Carla, plano.Id, "Feito.");

        var (reaberto, erro) = await svc.ReabrirAsync(plano.Id);
        Assert.Null(erro);
        Assert.Equal(VidaDoPlano.Ativo, reaberto!.Life);
        Assert.Null(reaberto.ClosedById);
        Assert.Null(reaberto.ClosedAt);
        // a evidência fica: ela é o registro do que foi feito, não do encerramento
        Assert.Equal("Feito.", reaberto.EvidenceNote);
    }

    [Fact]
    public async Task Plano_encerrado_com_acao_em_aberto_aparece_marcado()
    {
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);
        await svc.CriarAcaoAsync(Carla, plano.Id, Dados(dono.Id));
        await svc.EncerrarAsync(Carla, plano.Id, null);

        var lido = await svc.AbrirAsync(plano.Id);
        Assert.True(PlanoDoPlano.EncerradoComPendencia(lido!));
    }

    [Fact]
    public void Reabrir_e_do_gestor_ou_do_administrador()
    {
        Assert.True(PlanoDeAcaoService.CanReopen(Roles.SystemAdministrator));
        Assert.True(PlanoDeAcaoService.CanReopen(Roles.SupplyManager));
        Assert.False(PlanoDeAcaoService.CanReopen(Roles.Requester));
    }

    // ---- as ações do plano ---------------------------------------------------

    [Fact]
    public async Task A_acao_nasce_dentro_do_plano_numerada_e_com_dono_de_verdade()
    {
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);
        var (a, erro) = await svc.CriarAcaoAsync(Carla, plano.Id, Dados(dono.Id));

        Assert.Null(erro);
        Assert.Equal("AC-2026-000001", a!.Number);
        Assert.Equal(plano.Id, a.PlanId);
        Assert.Equal(1, a.Seq);
        // o dono é chave estrangeira; o nome é só o retrato do momento
        Assert.Equal(dono.Id, a.ResponsibleId);
        Assert.Equal("Carla Compradora", a.ResponsibleLabel);
    }

    [Fact]
    public async Task A_acao_sem_centro_herda_o_do_plano()
    {
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);
        var (a, _) = await svc.CriarAcaoAsync(Carla, plano.Id,
            Dados(dono.Id) with { CostCenter = null });
        Assert.Equal("CC-01", a!.CostCenter);
    }

    [Fact]
    public async Task A_sequencia_das_acoes_segue_a_ordem_em_que_foram_pensadas()
    {
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);
        await svc.CriarAcaoAsync(Carla, plano.Id, Dados(dono.Id));
        var (segunda, _) = await svc.CriarAcaoAsync(Carla, plano.Id, Dados(dono.Id));
        Assert.Equal(2, segunda!.Seq);
    }

    [Fact]
    public async Task Acao_em_plano_inexistente_e_recusada()
    {
        var (svc, _, dono) = Mundo();
        var (_, erro) = await svc.CriarAcaoAsync(Carla, Guid.NewGuid(), Dados(dono.Id));
        Assert.Equal("AP-ERR-404", erro!.Code);
    }

    [Fact]
    public async Task Responsavel_inativo_ou_inexistente_e_recusado()
    {
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);
        var (_, erro) = await svc.CriarAcaoAsync(Carla, plano.Id, Dados(Guid.NewGuid()));
        Assert.Equal("AC-ERR-011", erro!.Code);
    }

    [Fact]
    public async Task Prazo_antes_do_inicio_e_dado_trocado_e_nao_prazo_apertado()
    {
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);
        var (_, erro) = await svc.CriarAcaoAsync(Carla, plano.Id,
            Dados(dono.Id, inicio: Hoje.AddDays(5), prazo: Hoje.AddDays(1)));
        Assert.Equal("AC-ERR-012", erro!.Code);
    }

    [Fact]
    public async Task Suspender_e_cancelar_exigem_motivo()
    {
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);
        var (a, _) = await svc.CriarAcaoAsync(Carla, plano.Id, Dados(dono.Id));

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
        var plano = await PlanoAsync(svc, dono.Id);
        var (a, _) = await svc.CriarAcaoAsync(Carla, plano.Id, Dados(dono.Id));
        var (fechada, _) = await svc.MudarStatusAsync(a!.Id, StatusDaAcao.Concluida, null, 30);
        Assert.Equal(100, fechada!.Progress);
        Assert.NotNull(fechada.CompletedAt);
    }

    [Fact]
    public async Task A_numeracao_nao_repete()
    {
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);
        await svc.CriarAcaoAsync(Carla, plano.Id, Dados(dono.Id));
        var (segunda, _) = await svc.CriarAcaoAsync(Carla, plano.Id, Dados(dono.Id));
        Assert.Equal("AC-2026-000002", segunda!.Number);
    }

    // ---- riscos, causa raiz e lições -----------------------------------------

    [Fact]
    public async Task A_severidade_do_risco_e_calculada_e_nao_escolhida()
    {
        // dois riscos marcados "alto" que significam coisas diferentes é o que faz a matriz
        // deixar de servir para priorizar
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);

        var (critico, erro) = await svc.SalvarRiscoAsync(plano.Id, null,
            "A empilhadeira pode parar na alta", "MUITO_ALTA", "ALTA", "Contrato de reserva", null, null);
        Assert.Null(erro);
        Assert.Equal(12, SeveridadeDoRisco.Pontos(critico!));
        Assert.Equal("CRITICO", SeveridadeDoRisco.Rotulo(critico!));

        var (baixo, _) = await svc.SalvarRiscoAsync(plano.Id, null,
            "O cartaz pode descolar da parede", "BAIXA", "BAIXA", null, null, null);
        Assert.Equal("BAIXO", SeveridadeDoRisco.Rotulo(baixo!));
    }

    [Fact]
    public async Task A_causa_raiz_do_plano_e_uma_so_e_se_corrige()
    {
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);

        await svc.SalvarCausaRaizAsync(plano.Id, "CINCO_PORQUES", "{}", "Sem limite afixado");
        await svc.SalvarCausaRaizAsync(plano.Id, null, null, "Sem limite de empilhamento afixado");

        var lido = await svc.AbrirAsync(plano.Id);
        var causa = Assert.Single(lido!.RootCauses);
        Assert.Equal("Sem limite de empilhamento afixado", causa.MainCause);
        Assert.Equal("CINCO_PORQUES", causa.Method);
    }

    [Fact]
    public async Task As_licoes_guardam_o_que_nao_funcionou_com_o_mesmo_cuidado()
    {
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);
        await svc.SalvarLicoesAsync(plano.Id, "O cartaz funcionou",
            "O treinamento por vídeo não pegou", "Treinar na doca, não na sala", null, null, null);

        var lido = await svc.AbrirAsync(plano.Id);
        var licao = Assert.Single(lido!.Lessons);
        Assert.Contains("não pegou", licao.WhatFailed);
    }

    [Fact]
    public async Task Plano_encerrado_nao_recebe_risco_nem_causa_raiz()
    {
        var (svc, _, dono) = Mundo();
        var plano = await PlanoAsync(svc, dono.Id);
        await svc.EncerrarAsync(Carla, plano.Id, null);

        var (_, erroRisco) = await svc.SalvarRiscoAsync(plano.Id, null, "Qualquer risco aqui",
            null, null, null, null, null);
        Assert.Equal("AP-ERR-020", erroRisco!.Code);

        var (_, erroCausa) = await svc.SalvarCausaRaizAsync(plano.Id, null, null, "Qualquer causa");
        Assert.Equal("AP-ERR-020", erroCausa!.Code);
    }

    // ---- a lista -------------------------------------------------------------

    [Fact]
    public async Task O_filtro_de_situacao_usa_a_mesma_regua_do_placar()
    {
        var (svc, _, dono) = Mundo();
        var atrasado = await PlanoAsync(svc, dono.Id);
        // o início vai junto: prazo antes do começo é dado trocado, e o serviço recusa
        var (_, erro) = await svc.AtualizarPlanoAsync(atrasado.Id,
            DadosDoPlano() with { StartDate = Hoje.AddDays(-30), DueDate = Hoje.AddDays(-2) });
        Assert.Null(erro);
        await PlanoAsync(svc, dono.Id);

        var pagina = await svc.ListarAsync(new FiltroDePlanos(Situacao: SituacaoDoPlano.Atrasado));

        Assert.Equal([atrasado.Code], pagina.Itens.Select(p => p.Code));
        // e o placar do topo conta a mesma coisa que a lista abre
        Assert.Equal(1, pagina.Placar.Atrasados);
        Assert.Equal(2, pagina.Placar.Total);
    }
}
