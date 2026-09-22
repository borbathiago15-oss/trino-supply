using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Acoes;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Melhoria;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O ciclo de melhoria: encerramento (§4), mudança de escopo (§5) e visibilidade (§6).
///
/// <para>
/// A regra mais importante do módulo é a do encerramento: <b>prazo vencido não encerra nada</b>.
/// Quem encerra é uma pessoa, e ela diz três coisas — se a meta foi atingida, por quê, e, se
/// sobrou ação em aberto, que fecha assim mesmo.
/// </para>
/// </summary>
public class CicloDeMelhoriaTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Agora = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoje = new(2026, 9, 22);

    private static AppDbContext Banco() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CicloDeMelhoriaService Servico(AppDbContext db) => new(db, new FixedTimeProvider(Agora));

    private static async Task<(User user, Actor ator)> GenteAsync(
        AppDbContext db, string nome, string papel = Roles.Requester,
        Guid? setor = null, Guid? gestor = null, string? centros = null)
    {
        var u = new User
        {
            Email = $"{nome.ToLowerInvariant()}@t.com", Name = nome, Role = papel,
            SectorId = setor, SupplyManagerId = gestor, CostCenters = centros, PasswordHash = "x",
        };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return (u, new Actor(u.Id, u.Name, u.Role));
    }

    private static async Task<ImprovementCycle> CicloAsync(
        AppDbContext db, Actor ator, string titulo = "Reduzir avarias na doca 2",
        string escopo = EscopoDoCiclo.Gestao, IReadOnlyList<string>? centros = null)
    {
        var (ciclo, erro) = await Servico(db).CriarAsync(ator,
            Dados(titulo, escopo, centros));
        Assert.Null(erro);
        return ciclo!;
    }

    private static DadosDoCiclo Dados(
        string titulo, string escopo = EscopoDoCiclo.Gestao,
        IReadOnlyList<string>? centros = null, Guid? setor = null, string? fase = null) =>
        new(titulo, escopo, null, setor, null, null, null, null, null,
            null, null, null, null, null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, fase, centros);

    /// <summary>
    /// Uma ação do ciclo. Ela não aponta mais para o ciclo: vive dentro de um <b>plano</b>, e
    /// é o plano que diz de qual ciclo nasceu. O ajudante cria o plano na primeira chamada e
    /// o reaproveita nas seguintes, que é como o ciclo se comporta de verdade.
    /// </summary>
    private static async Task<ActionItem> AcaoAsync(
        AppDbContext db, Guid cicloId, Guid dono, string status = StatusDaAcao.Pendente,
        DateOnly? prazo = null, string? causa = null)
    {
        var plano = await db.ActionPlans.FirstOrDefaultAsync(p => p.CycleId == cicloId);
        if (plano is null)
        {
            plano = new ActionPlan
            {
                Code = "AP-2026-" + Guid.NewGuid().ToString("N")[..3],
                Title = "Plano do ciclo", CycleId = cicloId, CreatedByLabel = "Quem criou",
                CreatedAt = Agora, UpdatedAt = Agora,
            };
            db.ActionPlans.Add(plano);
            await db.SaveChangesAsync();
        }
        var planoId = plano.Id;
        var a = new ActionItem
        {
            Number = "AC-2026-" + Guid.NewGuid().ToString("N")[..6],
            Title = "Afixar o limite de empilhamento",
            ResponsibleId = dono, ResponsibleLabel = "Dono", CreatedByLabel = "Quem criou",
            PlanId = planoId, Status = status, DueDate = prazo, RootCauseRef = causa,
            CreatedAt = Agora, UpdatedAt = Agora,
        };
        db.ActionItems.Add(a);
        await db.SaveChangesAsync();
        return a;
    }

    // ---- §4 encerramento -----------------------------------------------------

    [Fact]
    public async Task Encerrar_sem_dizer_se_a_meta_foi_atingida_e_recusado()
    {
        // o veredito é de quem conduziu — não se deduz do indicador, que pode nem ter sido medido
        var db = Banco();
        var (_, ator) = await GenteAsync(db, "Ana");
        var ciclo = await CicloAsync(db, ator);

        var (fechado, erro, _) = await Servico(db).EncerrarAsync(ator, ciclo.Id,
            new PedidoDeEncerramento(null, "A meta foi perseguida e o resultado ficou aquém.", false));

        Assert.Null(fechado);
        Assert.Equal("PDCA-ERR-043", erro!.Code);
    }

    [Fact]
    public async Task Encerrar_sem_motivo_e_recusado()
    {
        var db = Banco();
        var (_, ator) = await GenteAsync(db, "Ana");
        var ciclo = await CicloAsync(db, ator);

        var (_, erro, _) = await Servico(db).EncerrarAsync(ator, ciclo.Id,
            new PedidoDeEncerramento(true, "ok", false));

        Assert.Equal("PDCA-ERR-040", erro!.Code);
        Assert.Contains("20", erro.Message);
    }

    [Fact]
    public async Task Encerrar_com_acao_em_aberto_sem_confirmar_e_recusado_e_diz_o_que_sobrou()
    {
        // pedir confirmação sem dizer do quê seria pedir uma assinatura em branco
        var db = Banco();
        var (u, ator) = await GenteAsync(db, "Ana");
        var ciclo = await CicloAsync(db, ator);
        await AcaoAsync(db, ciclo.Id, u.Id, StatusDaAcao.Concluida);
        await AcaoAsync(db, ciclo.Id, u.Id, StatusDaAcao.Pendente);
        await AcaoAsync(db, ciclo.Id, u.Id, StatusDaAcao.Suspensa);

        var (fechado, erro, pendentes) = await Servico(db).EncerrarAsync(ator, ciclo.Id,
            new PedidoDeEncerramento(false, "O ganho não veio e a equipe foi realocada.", false));

        Assert.Null(fechado);
        Assert.Equal("PDCA-ERR-041", erro!.Code);
        // suspensa continua aberta: ela não sumiu, só não corre
        Assert.Equal(2, pendentes.Count);
    }

    [Fact]
    public async Task Encerrar_com_acao_em_aberto_passa_com_a_confirmacao_e_a_lista_fica_no_motivo()
    {
        // o ciclo PODE fechar com pendência — às vezes é a decisão certa. O que não pode é
        // fechar sem ninguém assumir isso
        var db = Banco();
        var (u, ator) = await GenteAsync(db, "Ana");
        var ciclo = await CicloAsync(db, ator);
        var pendente = await AcaoAsync(db, ciclo.Id, u.Id);

        var (fechado, erro, _) = await Servico(db).EncerrarAsync(ator, ciclo.Id,
            new PedidoDeEncerramento(true, "A meta foi batida e o resto virou rotina da área.", true));

        Assert.Null(erro);
        Assert.Equal(FaseDoCiclo.Encerrado, fechado!.Phase);
        Assert.True(fechado.GoalMet);
        Assert.Equal(ator.Id, fechado.ClosedById);
        Assert.Contains(pendente.Number, fechado.ClosedReason);
    }

    [Fact]
    public async Task Prazo_vencido_nao_encerra_nada()
    {
        // ele é informação na tela, nunca veredito
        var db = Banco();
        var (_, ator) = await GenteAsync(db, "Ana");
        var ciclo = await CicloAsync(db, ator);
        ciclo.GoalDeadline = Hoje.AddDays(-30);
        await db.SaveChangesAsync();

        var aberto = await Servico(db).AbrirAsync(ator, ciclo.Id);
        Assert.Equal(FaseDoCiclo.Plan, aberto!.Ciclo.Phase);
        Assert.True(aberto.Leitura.Prazo.Vencido);
        Assert.Contains(aberto.Leitura.Sinais, s => s.Chave == "prazo-vencido");
    }

    [Fact]
    public async Task Mudar_a_fase_para_encerrado_pela_edicao_comum_e_recusado()
    {
        // era pela trilha de fases que o ciclo fechava sem registrar quem decidiu
        var db = Banco();
        var (_, ator) = await GenteAsync(db, "Ana");
        var ciclo = await CicloAsync(db, ator);

        var (_, erro) = await Servico(db).AtualizarAsync(ator, ciclo.Id,
            Dados("Reduzir avarias na doca 2", fase: FaseDoCiclo.Encerrado));

        Assert.Equal("PDCA-ERR-044", erro!.Code);
        Assert.Equal(FaseDoCiclo.Plan,
            (await db.ImprovementCycles.SingleAsync(c => c.Id == ciclo.Id)).Phase);
    }

    [Fact]
    public async Task Reabrir_apaga_o_veredito_anterior()
    {
        // quem/quando/por quê não valem mais para o ciclo em curso
        var db = Banco();
        var (_, ator) = await GenteAsync(db, "Ana");
        var ciclo = await CicloAsync(db, ator);
        await Servico(db).EncerrarAsync(ator, ciclo.Id,
            new PedidoDeEncerramento(true, "A meta foi batida e virou procedimento.", false));

        var (reaberto, erro) = await Servico(db).ReabrirAsync(ciclo.Id, null);

        Assert.Null(erro);
        Assert.Equal(FaseDoCiclo.Act, reaberto!.Phase);
        Assert.Null(reaberto.GoalMet);
        Assert.Null(reaberto.ClosedAt);
        Assert.Null(reaberto.ClosedById);
        Assert.Null(reaberto.ClosedReason);
    }

    [Fact]
    public async Task Ciclo_encerrado_com_pendencia_aparece_marcado()
    {
        // a tela expõe a contradição em vez de escondê-la
        var db = Banco();
        var (u, ator) = await GenteAsync(db, "Ana");
        var ciclo = await CicloAsync(db, ator);
        await AcaoAsync(db, ciclo.Id, u.Id);
        await Servico(db).EncerrarAsync(ator, ciclo.Id,
            new PedidoDeEncerramento(false, "Encerrado para o time tocar outra frente.", true));

        var aberto = await Servico(db).AbrirAsync(ator, ciclo.Id);
        Assert.Contains(aberto!.Leitura.Sinais, s => s.Chave == "encerrado-com-pendencia");

        var (_, placar) = await Servico(db).ListarAsync(ator, new FiltroDeCiclos());
        Assert.Equal(1, placar.EncerradosComPendencia);
    }

    [Fact]
    public async Task Ciclo_ja_encerrado_nao_encerra_de_novo()
    {
        var db = Banco();
        var (_, ator) = await GenteAsync(db, "Ana");
        var ciclo = await CicloAsync(db, ator);
        var pedido = new PedidoDeEncerramento(true, "A meta foi batida e virou procedimento.", false);
        await Servico(db).EncerrarAsync(ator, ciclo.Id, pedido);

        var (_, erro, _) = await Servico(db).EncerrarAsync(ator, ciclo.Id, pedido);
        Assert.Equal("PDCA-ERR-042", erro!.Code);
    }

    // ---- §5 mudança de escopo ------------------------------------------------

    [Fact]
    public async Task Mudar_o_escopo_leva_as_acoes_junto()
    {
        var db = Banco();
        var (u, ator) = await GenteAsync(db, "Ana");
        var ciclo = await CicloAsync(db, ator, escopo: EscopoDoCiclo.Centro, centros: ["BAH-001"]);
        var acao = await AcaoAsync(db, ciclo.Id, u.Id);
        acao.CostCenter = "BAH-001";
        await db.SaveChangesAsync();

        var (mudado, movidas, erro) = await Servico(db)
            .MudarEscopoAsync(ciclo.Id, EscopoDoCiclo.Centro, ["PB-007"]);

        Assert.Null(erro);
        Assert.Equal(1, movidas);
        Assert.Equal("PB-007", (await db.ActionItems.SingleAsync(a => a.Id == acao.Id)).CostCenter);
        Assert.Equal(["PB-007"], mudado!.CostCenters.Select(x => x.CostCenter));
    }

    [Fact]
    public async Task Mudar_para_gestao_tira_o_centro_das_acoes()
    {
        var db = Banco();
        var (u, ator) = await GenteAsync(db, "Ana");
        var ciclo = await CicloAsync(db, ator, escopo: EscopoDoCiclo.Centro, centros: ["BAH-001"]);
        var acao = await AcaoAsync(db, ciclo.Id, u.Id);
        acao.CostCenter = "BAH-001";
        await db.SaveChangesAsync();

        var (_, _, erro) = await Servico(db).MudarEscopoAsync(ciclo.Id, EscopoDoCiclo.Gestao, null);

        Assert.Null(erro);
        Assert.Null((await db.ActionItems.SingleAsync(a => a.Id == acao.Id)).CostCenter);
    }

    [Fact]
    public async Task Destino_ambiguo_nao_move_nada()
    {
        // com dois centros de destino não há para onde mandar cada ação, e escolher por ela
        // seria inventar o dado
        var db = Banco();
        var (u, ator) = await GenteAsync(db, "Ana");
        var ciclo = await CicloAsync(db, ator, escopo: EscopoDoCiclo.Centro, centros: ["BAH-001"]);
        var acao = await AcaoAsync(db, ciclo.Id, u.Id);
        acao.CostCenter = "BAH-001";
        await db.SaveChangesAsync();

        var (_, movidas, erro) = await Servico(db)
            .MudarEscopoAsync(ciclo.Id, EscopoDoCiclo.Multi, ["PB-007", "SE-002"]);

        Assert.Equal("PDCA-ERR-052", erro!.Code);
        Assert.Equal(0, movidas);
        Assert.Equal("BAH-001", (await db.ActionItems.SingleAsync(a => a.Id == acao.Id)).CostCenter);
    }

    [Fact]
    public void Mudar_o_escopo_e_escrever_dado_de_centro_de_custo()
    {
        Assert.True(CicloDeMelhoriaService.PodeMudarEscopo(Roles.SupplyManager));
        Assert.True(CicloDeMelhoriaService.PodeMudarEscopo(Roles.SystemAdministrator));
        Assert.False(CicloDeMelhoriaService.PodeMudarEscopo(Roles.Requester));
        Assert.False(CicloDeMelhoriaService.PodeMudarEscopo(Roles.PurchasingOfficer));
    }

    // ---- §6 visibilidade -----------------------------------------------------

    private static async Task<bool> VeAsync(AppDbContext db, User eu, Guid cicloId)
    {
        var visiveis = await Servico(db).VisiveisAsync(eu);
        return await visiveis.AnyAsync(c => c.Id == cicloId);
    }

    [Fact]
    public async Task Quem_criou_o_ciclo_o_enxerga()
    {
        var db = Banco();
        var (ana, ator) = await GenteAsync(db, "Ana");
        var ciclo = await CicloAsync(db, ator);
        Assert.True(await VeAsync(db, ana, ciclo.Id));
    }

    [Fact]
    public async Task Quem_nao_tem_elo_nenhum_nao_enxerga()
    {
        var db = Banco();
        var (_, ator) = await GenteAsync(db, "Ana");
        var (bruno, _) = await GenteAsync(db, "Bruno");
        var ciclo = await CicloAsync(db, ator);
        Assert.False(await VeAsync(db, bruno, ciclo.Id));
    }

    [Fact]
    public async Task O_dono_do_ciclo_o_enxerga_mesmo_sem_te_lo_criado()
    {
        var db = Banco();
        var (_, ator) = await GenteAsync(db, "Ana");
        var (bruno, _) = await GenteAsync(db, "Bruno");
        var (ciclo, erro) = await Servico(db).CriarAsync(ator,
            Dados("Reduzir avarias na doca 2") with { OwnerId = bruno.Id });
        Assert.Null(erro);
        Assert.True(await VeAsync(db, bruno, ciclo!.Id));
    }

    [Fact]
    public async Task Quem_foi_marcado_em_quem_mais_acompanha_enxerga()
    {
        // para quem não conduz, esta lista É o acesso
        var db = Banco();
        var (_, ator) = await GenteAsync(db, "Ana");
        var (bruno, _) = await GenteAsync(db, "Bruno");
        var ciclo = await CicloAsync(db, ator);
        Assert.False(await VeAsync(db, bruno, ciclo.Id));

        await Servico(db).DefinirAcompanhantesAsync(ator, ciclo.Id, [bruno.Id]);
        Assert.True(await VeAsync(db, bruno, ciclo.Id));
    }

    [Fact]
    public async Task Quem_responde_por_uma_acao_do_ciclo_enxerga()
    {
        var db = Banco();
        var (_, ator) = await GenteAsync(db, "Ana");
        var (bruno, _) = await GenteAsync(db, "Bruno");
        var ciclo = await CicloAsync(db, ator);
        await AcaoAsync(db, ciclo.Id, bruno.Id);
        Assert.True(await VeAsync(db, bruno, ciclo.Id));
    }

    [Fact]
    public async Task O_colega_do_mesmo_setor_enxerga_o_ciclo_do_setor()
    {
        // é a razão de o escopo "setor" existir: sem ele, o ciclo do setor caía em "gestão"
        // e o colega do próprio setor não o via
        var db = Banco();
        var setor = new Sector { Code = "TI", Name = "Tecnologia" };
        db.Sectors.Add(setor);
        await db.SaveChangesAsync();

        var (_, ator) = await GenteAsync(db, "Ana", setor: setor.Id);
        var (bruno, _) = await GenteAsync(db, "Bruno", setor: setor.Id);
        var (carla, _) = await GenteAsync(db, "Carla");
        var ciclo = await CicloAsync(db, ator, escopo: EscopoDoCiclo.Setor);

        Assert.True(await VeAsync(db, bruno, ciclo.Id));
        Assert.False(await VeAsync(db, carla, ciclo.Id));
    }

    [Fact]
    public async Task Gestao_nao_e_publico()
    {
        // o plano da casa entrava na lista de qualquer perfil só por não ter centro vinculado
        var db = Banco();
        var (_, ator) = await GenteAsync(db, "Ana");
        var (bruno, _) = await GenteAsync(db, "Bruno", Roles.Approver);
        var ciclo = await CicloAsync(db, ator, escopo: EscopoDoCiclo.Gestao);
        Assert.False(await VeAsync(db, bruno, ciclo.Id));
    }

    [Fact]
    public async Task Centro_vinculado_nao_abre_ciclo_para_perfil_operacional()
    {
        // ter o centro na lista deixou de bastar: o ciclo de um centro pode tratar de assunto
        // que não é de todo mundo
        var db = Banco();
        var (_, ator) = await GenteAsync(db, "Ana");
        var (bruno, _) = await GenteAsync(db, "Bruno", Roles.Requester, centros: "BAH-001");
        var ciclo = await CicloAsync(db, ator, escopo: EscopoDoCiclo.Centro, centros: ["BAH-001"]);
        Assert.False(await VeAsync(db, bruno, ciclo.Id));
    }

    [Fact]
    public async Task O_gestor_soma_os_centros_dele_e_o_que_a_equipe_abriu()
    {
        var db = Banco();
        var (gestor, _) = await GenteAsync(db, "Gustavo", Roles.SupplyManager, centros: "BAH-001");
        var (_, ana) = await GenteAsync(db, "Ana", gestor: gestor.Id);
        var (_, dora) = await GenteAsync(db, "Dora");

        var doCentro = await CicloAsync(db, dora, escopo: EscopoDoCiclo.Centro, centros: ["BAH-001"]);
        var daEquipe = await CicloAsync(db, ana, titulo: "Reduzir retrabalho na conferência");
        var deFora = await CicloAsync(db, dora, titulo: "Reestruturar a área comercial inteira");

        Assert.True(await VeAsync(db, gestor, doCentro.Id));
        Assert.True(await VeAsync(db, gestor, daEquipe.Id));
        Assert.False(await VeAsync(db, gestor, deFora.Id));
    }

    [Fact]
    public async Task A_visibilidade_sobe_e_nao_desce()
    {
        // o gestor vê o que a equipe abriu; o ciclo dele só aparece para quem ele marcou
        var db = Banco();
        var (gestor, gestorAtor) = await GenteAsync(db, "Gustavo", Roles.SupplyManager);
        var (ana, _) = await GenteAsync(db, "Ana", gestor: gestor.Id);
        var doGestor = await CicloAsync(db, gestorAtor);

        Assert.False(await VeAsync(db, ana, doGestor.Id));
    }

    [Fact]
    public async Task O_administrador_enxerga_tudo()
    {
        var db = Banco();
        var (_, ator) = await GenteAsync(db, "Ana");
        var (admin, _) = await GenteAsync(db, "Admin", Roles.SystemAdministrator);
        var ciclo = await CicloAsync(db, ator);
        Assert.True(await VeAsync(db, admin, ciclo.Id));
    }

    [Fact]
    public async Task Abrir_um_ciclo_que_nao_se_enxerga_devolve_nada()
    {
        // a mesma régua da lista: uma lista que mostra o que não se pode abrir mente
        var db = Banco();
        var (_, ator) = await GenteAsync(db, "Ana");
        var (_, bruno) = await GenteAsync(db, "Bruno");
        var ciclo = await CicloAsync(db, ator);
        Assert.Null(await Servico(db).AbrirAsync(bruno, ciclo.Id));
    }

    // ---- quem escreve o quê --------------------------------------------------

    [Fact]
    public async Task Quem_nao_conduz_nao_marca_quem_acompanha()
    {
        // deixá-los editar a própria lista de acesso seria não ter regra nenhuma
        var db = Banco();
        var (_, ator) = await GenteAsync(db, "Ana");
        var (bruno, brunoAtor) = await GenteAsync(db, "Bruno");
        var ciclo = await CicloAsync(db, ator);

        var (_, erro) = await Servico(db).DefinirAcompanhantesAsync(brunoAtor, ciclo.Id, [bruno.Id]);
        Assert.Equal("PDCA-ERR-061", erro!.Code);
    }

    [Fact]
    public async Task Quem_tem_setor_nao_escolhe_o_setor_do_ciclo()
    {
        // deixar escolher abriria a porta de pendurar trabalho no setor alheio
        var db = Banco();
        var meu = new Sector { Code = "TI", Name = "Tecnologia" };
        var alheio = new Sector { Code = "RH", Name = "Recursos Humanos" };
        db.Sectors.AddRange(meu, alheio);
        await db.SaveChangesAsync();

        var (_, ator) = await GenteAsync(db, "Ana", setor: meu.Id);
        var (ciclo, erro) = await Servico(db).CriarAsync(ator,
            Dados("Reduzir avarias na doca 2", setor: alheio.Id));

        Assert.Null(erro);
        Assert.Equal(meu.Id, ciclo!.SectorId);
    }

    [Fact]
    public async Task Quem_nao_tem_setor_escolhe_um_do_cadastro_e_so_ativo()
    {
        var db = Banco();
        var ativo = new Sector { Code = "TI", Name = "Tecnologia" };
        var inativo = new Sector { Code = "RH", Name = "Recursos Humanos", Active = false };
        db.Sectors.AddRange(ativo, inativo);
        await db.SaveChangesAsync();

        var (_, ator) = await GenteAsync(db, "Ana");
        var (ciclo, _) = await Servico(db).CriarAsync(ator,
            Dados("Reduzir avarias na doca 2", setor: ativo.Id));
        Assert.Equal(ativo.Id, ciclo!.SectorId);

        var (_, erro) = await Servico(db).CriarAsync(ator,
            Dados("Reduzir retrabalho na conferência", setor: inativo.Id));
        Assert.Equal("PDCA-ERR-014", erro!.Code);
    }

    // ---- o título e o código -------------------------------------------------

    [Fact]
    public async Task O_titulo_e_uma_frase_e_nao_um_rotulo()
    {
        var db = Banco();
        var (_, ator) = await GenteAsync(db, "Ana");
        var (_, erro) = await Servico(db).CriarAsync(ator, Dados("Avarias"));
        Assert.Equal("PDCA-ERR-010", erro!.Code);
    }

    [Fact]
    public async Task O_codigo_nao_se_repete()
    {
        var db = Banco();
        var (_, ator) = await GenteAsync(db, "Ana");
        var a = await CicloAsync(db, ator, "Reduzir avarias na doca 2");
        var b = await CicloAsync(db, ator, "Reduzir retrabalho na conferência");
        Assert.Equal("PDCA-2026-001", a.Code);
        Assert.Equal("PDCA-2026-002", b.Code);
    }

    [Fact]
    public async Task A_causa_raiz_dos_cinco_porques_preenche_a_do_ciclo()
    {
        // é o mesmo dado; digitá-lo duas vezes é convite a divergir
        var db = Banco();
        var (_, ator) = await GenteAsync(db, "Ana");
        var ciclo = await CicloAsync(db, ator);

        var (atualizado, _) = await Servico(db).AtualizarAsync(ator, ciclo.Id,
            Dados("Reduzir avarias na doca 2") with
            {
                ToolName = FerramentaDeCausa.CincoPorques,
                ToolData = """{"causa_raiz":"Não há limite de empilhamento afixado"}""",
            });

        Assert.Equal("Não há limite de empilhamento afixado", atualizado!.RootCause);
    }
}
