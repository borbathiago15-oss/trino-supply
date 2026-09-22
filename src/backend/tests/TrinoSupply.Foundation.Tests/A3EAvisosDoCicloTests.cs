using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Acoes;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Melhoria;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O A3 (§7) e os avisos do dono (§9.2).
///
/// <para>
/// O A3 sai da <b>mesma</b> leitura que a tela mostra. Papel e tela dizendo números diferentes
/// parariam a reunião para descobrir em qual acreditar, e nenhum dos dois voltaria a ser levado
/// a sério.
/// </para>
/// </summary>
public class A3EAvisosDoCicloTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Agora = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoje = new(2026, 9, 22);

    private static AppDbContext Banco() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ImprovementCycle Ciclo(
        Guid? dono = null, string fase = FaseDoCiclo.Do,
        string? ferramenta = null, string? dados = null)
    {
        var c = CicloBase(dono, fase);
        if (ferramenta is not null)
            c.Tools.Add(new CycleTool { CycleId = c.Id, ToolType = ferramenta, ToolData = dados });
        return c;
    }

    private static ImprovementCycle CicloBase(Guid? dono, string fase) => new()
    {
        Code = "PDCA-2026-001", Title = "Reduzir avarias na doca 2",
        Phase = fase, Scope = EscopoDoCiclo.Gestao,
        Indicator = "Avarias/mês", Baseline = 40, GoalValue = 10, Unit = "un",
        Problem = "Avarias sobem desde julho", RootCause = "Empilhamento acima do limite",
        OwnerId = dono, OwnerLabel = "Ana", CreatedByLabel = "Ana",
        CreatedAt = Agora.AddDays(-20), UpdatedAt = Agora,
    };

    private static CicloCompleto Completo(ImprovementCycle c, params ActionItem[] acoes)
    {
        var analises = CicloDeMelhoriaService.Analises(c);
        return new(c, analises, acoes, MotorDeLeitura.Ler(c, analises, acoes, Hoje), null);
    }

    // ---- §7 o A3 -------------------------------------------------------------

    [Fact]
    public void O_A3_sai_como_pdf_de_uma_folha()
    {
        var pdf = A3DoCiclo.Gerar(Completo(Ciclo()), Hoje);
        Assert.NotEmpty(pdf);
        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
    }

    [Fact]
    public void O_A3_desenha_qualquer_ferramenta_sem_quebrar()
    {
        // as cinco passam pelo mesmo desenho: uma que estourasse só apareceria na reunião
        var casos = new (string Nome, string Dados)[]
        {
            (FerramentaDeCausa.Pareto, """{"itens":[{"causa":"Manuseio","valor":50},{"causa":"Transporte","valor":10}]}"""),
            (FerramentaDeCausa.Gut, """{"itens":[{"problema":"Fila","g":5,"u":4,"t":3}]}"""),
            (FerramentaDeCausa.CincoPorques, """{"problema":"Avarias","porque1":"Por quê?","resposta1":"Caiu","causa_raiz":"Sem limite afixado"}"""),
            (FerramentaDeCausa.Ishikawa, """{"efeito":"Avarias","metodo":["Sem procedimento"]}"""),
            (FerramentaDeCausa.Brainstorming, """{"ideias":["Trocar o filme"]}"""),
            (FerramentaDeCausa.CincoWDoisH, """{"linhas":[{"oque":"Afixar o cartaz","quem":"Ana","quando":"10/10"}]}"""),
            (FerramentaDeCausa.Kaizen, """{"antes":"Palete solto","depois":"Palete cintado","melhorias":["Cinta"],"resultados":"Zero avaria"}"""),
            (FerramentaDeCausa.Fluxograma, """{"atual":["Recebe","Empilha"],"proposto":["Recebe","Confere","Empilha"]}"""),
        };
        foreach (var (nome, dados) in casos)
            Assert.NotEmpty(A3DoCiclo.Gerar(Completo(Ciclo(ferramenta: nome, dados: dados)), Hoje));

        // e as oito juntas na mesma folha, que é o caso que o Trino Intelligence trata
        var folha = Ciclo();
        var seq = 1;
        foreach (var (nome, dados) in casos)
            folha.Tools.Add(new CycleTool { CycleId = folha.Id, ToolType = nome, ToolData = dados, Seq = seq++ });
        Assert.NotEmpty(A3DoCiclo.Gerar(Completo(folha), Hoje));
    }

    [Fact]
    public void O_A3_de_um_ciclo_vazio_nao_quebra()
    {
        // ciclo recém-aberto é o caso mais comum de alguém pedir o A3 para levar à reunião
        var c = new ImprovementCycle { Code = "PDCA-2026-002", Title = "Ciclo em branco", CreatedByLabel = "Ana" };
        Assert.NotEmpty(A3DoCiclo.Gerar(Completo(c), Hoje));
    }

    [Fact]
    public void O_A3_usa_a_mesma_leitura_da_tela()
    {
        // não é um teste de pixel: é a garantia de que o papel lê o mesmo motor. Se a leitura
        // mudar de forma, isto quebra junto e ninguém publica um A3 com número velho
        var c = Ciclo(ferramenta: FerramentaDeCausa.Gut,
            dados: """{"itens":[{"problema":"Empilhamento acima do limite","g":5,"u":5,"t":5}]}""");
        var completo = Completo(c);

        Assert.Contains(completo.Leitura.Sinais, s => s.Chave == "causa-vital-sem-acao");
        Assert.NotEmpty(A3DoCiclo.Gerar(completo, Hoje));
    }

    // ---- §9.2 os avisos do dono ---------------------------------------------

    private static async Task<(AppDbContext db, Guid dono)> ComDonoAsync()
    {
        var db = Banco();
        var dono = new User { Email = "ana@t.com", Name = "Ana", PasswordHash = "x" };
        db.Users.Add(dono);
        await db.SaveChangesAsync();
        return (db, dono.Id);
    }

    private static AvisoDoCicloService Avisos(AppDbContext db) =>
        new(db, new AvisoDoUsuarioService(db, new FixedTimeProvider(Agora)), new FixedTimeProvider(Agora));

    [Fact]
    public async Task O_dono_e_avisado_do_ciclo_parado()
    {
        var (db, dono) = await ComDonoAsync();
        var c = Ciclo(dono);
        c.UpdatedAt = Agora.AddDays(-AvisoDoCicloService.DiasParado - 5);
        db.ImprovementCycles.Add(c);
        await db.SaveChangesAsync();

        await Avisos(db).AvaliarAsync(dono);

        var aviso = await db.UserNotices.SingleAsync();
        Assert.Equal(AvisoDoCicloKinds.CicloParado, aviso.Kind);
        Assert.Equal(dono, aviso.UserId);
        Assert.Contains("35 dias", aviso.Title);
    }

    [Fact]
    public async Task Ciclo_tocado_recentemente_nao_gera_aviso()
    {
        var (db, dono) = await ComDonoAsync();
        db.ImprovementCycles.Add(Ciclo(dono));
        await db.SaveChangesAsync();

        await Avisos(db).AvaliarAsync(dono);
        Assert.Empty(db.UserNotices);
    }

    [Fact]
    public async Task O_dono_e_avisado_do_check_vencido_sem_medicao()
    {
        var (db, dono) = await ComDonoAsync();
        var c = Ciclo(dono, FaseDoCiclo.Check);
        c.GoalDeadline = Hoje.AddDays(-3);
        db.ImprovementCycles.Add(c);
        await db.SaveChangesAsync();

        await Avisos(db).AvaliarAsync(dono);

        var aviso = await db.UserNotices.SingleAsync();
        Assert.Equal(AvisoDoCicloKinds.CheckVencido, aviso.Kind);
        // o aviso repete a regra: o prazo não encerra nada
        Assert.Contains("não encerra o ciclo", aviso.Body);
    }

    [Fact]
    public async Task Prazo_vencido_com_medicao_nao_gera_aviso_de_check()
    {
        var (db, dono) = await ComDonoAsync();
        var c = Ciclo(dono, FaseDoCiclo.Check);
        c.GoalDeadline = Hoje.AddDays(-3);
        c.ResultValue = 8;
        db.ImprovementCycles.Add(c);
        await db.SaveChangesAsync();

        await Avisos(db).AvaliarAsync(dono);
        Assert.Empty(db.UserNotices);
    }

    [Fact]
    public async Task Ciclo_encerrado_nao_avisa_nada()
    {
        // cobrar quem já fechou é barulho: a decisão foi tomada
        var (db, dono) = await ComDonoAsync();
        var c = Ciclo(dono, FaseDoCiclo.Encerrado);
        c.UpdatedAt = Agora.AddDays(-200);
        c.GoalDeadline = Hoje.AddDays(-90);
        db.ImprovementCycles.Add(c);
        await db.SaveChangesAsync();

        await Avisos(db).AvaliarAsync(dono);
        Assert.Empty(db.UserNotices);
    }

    [Fact]
    public async Task Abrir_a_caixa_duas_vezes_nao_duplica_o_aviso()
    {
        // sem a deduplicação, o mesmo ciclo parado renderia um aviso por visita, e repetir
        // não faz o atraso ser atendido: faz a caixa ser ignorada
        var (db, dono) = await ComDonoAsync();
        var c = Ciclo(dono);
        c.UpdatedAt = Agora.AddDays(-40);
        db.ImprovementCycles.Add(c);
        await db.SaveChangesAsync();

        await Avisos(db).AvaliarAsync(dono);
        await Avisos(db).AvaliarAsync(dono);

        Assert.Single(db.UserNotices);
    }

    [Fact]
    public async Task Prazo_novo_e_fato_novo_e_merece_aviso_novo()
    {
        var (db, dono) = await ComDonoAsync();
        var c = Ciclo(dono, FaseDoCiclo.Check);
        c.GoalDeadline = Hoje.AddDays(-10);
        db.ImprovementCycles.Add(c);
        await db.SaveChangesAsync();
        await Avisos(db).AvaliarAsync(dono);

        c.GoalDeadline = Hoje.AddDays(-1);
        await db.SaveChangesAsync();
        await Avisos(db).AvaliarAsync(dono);

        Assert.Equal(2, await db.UserNotices.CountAsync());
    }

    [Fact]
    public async Task O_ciclo_de_outra_pessoa_nao_entra_na_minha_caixa()
    {
        var (db, dono) = await ComDonoAsync();
        var outro = new User { Email = "bruno@t.com", Name = "Bruno", PasswordHash = "x" };
        db.Users.Add(outro);
        var c = Ciclo(outro.Id);
        c.UpdatedAt = Agora.AddDays(-90);
        db.ImprovementCycles.Add(c);
        await db.SaveChangesAsync();

        await Avisos(db).AvaliarAsync(dono);
        Assert.Empty(db.UserNotices);
    }
}
