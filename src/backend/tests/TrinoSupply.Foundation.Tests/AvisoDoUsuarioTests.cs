using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// Os cinco avisos do fluxo, e o escalonamento do prazo.
///
/// A Central de Avisos que já existia é derivada: conta o que está aberto e o número muda
/// sozinho. Serve para "o que há para eu fazer", não para "o que aconteceu comigo" — um
/// contador não avisa que a SC <b>passou</b> a ser sua. O que estes testes protegem é o aviso
/// ser um fato datado, com dono e com lido, e não nascer duas vezes.
/// </summary>
public class AvisoDoUsuarioTests
{
    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = agora;
        public override DateTimeOffset GetUtcNow() => Agora;
    }

    private sealed class NumerosFalsos : IPrNumberGenerator
    {
        private int _proximo;
        public Task<string> NextAsync(CancellationToken ct = default) =>
            Task.FromResult($"PR-2026-{++_proximo:000000}");
    }

    private static readonly DateTimeOffset Dia1 = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed record Mundo(
        AppDbContext Db, RelogioFixo Relogio, AvisoDoUsuarioService Avisos,
        RequisitionService Prs, TriageService Triagem, QuotationService Rfq,
        SupplierService Sup, TorreDeControleService Torre,
        Actor Ana, Actor Gestor, Actor Comprador);

    private static async Task<Mundo> BuildAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var relogio = new RelogioFixo(Dia1);

        // usuários de verdade: o aviso é endereçado, e endereço só existe com cadastro
        var ana = new User { Name = "Ana", Email = "ana@t.com", Role = Roles.Requester, Active = true };
        var gestor = new User { Name = "Marina", Email = "m@t.com", Role = Roles.SupplyManager, Active = true };
        var comprador = new User { Name = "Carla", Email = "c@t.com", Role = Roles.PurchasingOfficer, Active = true };
        db.Users.AddRange(ana, gestor, comprador);
        await db.SaveChangesAsync();

        return new Mundo(db, relogio, new AvisoDoUsuarioService(db, relogio),
            new RequisitionService(db, new NumerosFalsos(), new CatalogService(db, relogio), relogio),
            new TriageService(db, relogio), new QuotationService(db, relogio),
            new SupplierService(db, relogio), new TorreDeControleService(db, relogio),
            new Actor(ana.Id, ana.Name, ana.Role),
            new Actor(gestor.Id, gestor.Name, gestor.Role),
            new Actor(comprador.Id, comprador.Name, comprador.Role));
    }

    private static async Task<PurchaseRequisition> ScEnviadaAsync(Mundo w)
    {
        var (pr, _) = await w.Prs.CreateAsync(w.Ana, "Reposição", "CC-01", "NORMAL", null,
            [new ItemInput("Martelete", 1, "UN", 100, null)]);
        await w.Prs.SubmitAsync(w.Ana, pr!.Id);
        return pr;
    }

    [Fact]
    public async Task Aviso_1_a_SC_enviada_avisa_o_gestor_que_falta_comprador()
    {
        var w = await BuildAsync();
        await ScEnviadaAsync(w);

        var doGestor = await w.Avisos.DaPessoaAsync(w.Gestor.Id);

        var aviso = Assert.Single(doGestor);
        Assert.Equal(AvisoKinds.DemandaParaTriar, aviso.Kind);
        Assert.Contains("PR-2026-000001", aviso.Title);
        Assert.Null(aviso.ReadAt);
    }

    [Fact]
    public async Task O_solicitante_nao_recebe_o_aviso_do_gestor()
    {
        // aviso tem um dono só: mandar para todo mundo faria a caixa virar ruído
        var w = await BuildAsync();
        await ScEnviadaAsync(w);

        Assert.Empty(await w.Avisos.DaPessoaAsync(w.Ana.Id));
        Assert.Empty(await w.Avisos.DaPessoaAsync(w.Comprador.Id));
    }

    [Fact]
    public async Task Aviso_2_atribuir_avisa_o_comprador_que_recebeu()
    {
        var w = await BuildAsync();
        var pr = await ScEnviadaAsync(w);

        await w.Triagem.AssignAsync(w.Gestor, "SC", pr.Id, w.Comprador.Id);

        var aviso = Assert.Single(await w.Avisos.DaPessoaAsync(w.Comprador.Id));
        Assert.Equal(AvisoKinds.DemandaAtribuida, aviso.Kind);
        Assert.Contains("Marina", aviso.Body);   // quem atribuiu
    }

    [Fact]
    public async Task Atribuir_duas_vezes_a_mesma_pessoa_nao_duplica_o_aviso()
    {
        // reatribuir por engano e desfazer não pode encher a caixa do comprador
        var w = await BuildAsync();
        var pr = await ScEnviadaAsync(w);

        await w.Triagem.AssignAsync(w.Gestor, "SC", pr.Id, w.Comprador.Id);
        await w.Triagem.AssignAsync(w.Gestor, "SC", pr.Id, null);
        await w.Triagem.AssignAsync(w.Gestor, "SC", pr.Id, w.Comprador.Id);

        Assert.Single(await w.Avisos.DaPessoaAsync(w.Comprador.Id));
    }

    [Fact]
    public async Task Cada_pessoa_recebe_o_seu_e_marcar_lido_nao_apaga_o_dos_outros()
    {
        var w = await BuildAsync();
        var outroGestor = new User
        { Name = "Paulo", Email = "p@t.com", Role = Roles.SupplyManager, Active = true };
        w.Db.Users.Add(outroGestor);
        await w.Db.SaveChangesAsync();
        await ScEnviadaAsync(w);

        var meu = (await w.Avisos.DaPessoaAsync(w.Gestor.Id)).Single();
        await w.Avisos.MarcarLidoAsync(w.Gestor.Id, meu.Id);

        Assert.Equal(0, await w.Avisos.NaoLidosAsync(w.Gestor.Id));
        Assert.Equal(1, await w.Avisos.NaoLidosAsync(outroGestor.Id));
    }

    [Fact]
    public async Task Ninguem_marca_como_lido_o_aviso_de_outra_pessoa()
    {
        var w = await BuildAsync();
        await ScEnviadaAsync(w);
        var doGestor = (await w.Avisos.DaPessoaAsync(w.Gestor.Id)).Single();

        var ok = await w.Avisos.MarcarLidoAsync(w.Comprador.Id, doGestor.Id);

        Assert.False(ok);
        Assert.Equal(1, await w.Avisos.NaoLidosAsync(w.Gestor.Id));
    }

    [Fact]
    public async Task Marcar_tudo_lido_zera_a_caixa_de_quem_pediu()
    {
        var w = await BuildAsync();
        await ScEnviadaAsync(w);
        await ScEnviadaAsync(w);

        var quantos = await w.Avisos.MarcarTudoLidoAsync(w.Gestor.Id);

        Assert.Equal(2, quantos);
        Assert.Equal(0, await w.Avisos.NaoLidosAsync(w.Gestor.Id));
        Assert.Equal(2, (await w.Avisos.DaPessoaAsync(w.Gestor.Id)).Count);   // continuam legíveis
    }

    [Fact]
    public async Task Aviso_ja_lido_nao_volta_a_ficar_por_ler()
    {
        var w = await BuildAsync();
        await ScEnviadaAsync(w);
        var aviso = (await w.Avisos.DaPessoaAsync(w.Gestor.Id)).Single();
        await w.Avisos.MarcarLidoAsync(w.Gestor.Id, aviso.Id);
        var lidoEm = (await w.Avisos.DaPessoaAsync(w.Gestor.Id)).Single().ReadAt;

        w.Relogio.Agora = Dia1.AddDays(1);
        await w.Avisos.MarcarLidoAsync(w.Gestor.Id, aviso.Id);

        Assert.Equal(lidoEm, (await w.Avisos.DaPessoaAsync(w.Gestor.Id)).Single().ReadAt);
    }

    [Fact]
    public async Task Sem_gestor_cadastrado_ninguem_recebe_e_nada_quebra()
    {
        // o fato é mais importante que o recado: a SC precisa ser enviada de qualquer forma
        var w = await BuildAsync();
        foreach (var g in w.Db.Users.Where(u => u.Role == Roles.SupplyManager)) g.Active = false;
        await w.Db.SaveChangesAsync();

        var pr = await ScEnviadaAsync(w);

        Assert.Equal(RequisitionStatus.Submitted, pr.Status);
        Assert.Empty(await w.Avisos.DaPessoaAsync(w.Gestor.Id));
    }

    // ---- escalonamento ------------------------------------------------------

    [Fact]
    public async Task O_estouro_do_prazo_avisa_o_gestor_com_de_quem_se_espera()
    {
        // "SC estourou o prazo" sozinho manda o gestor abrir a Torre para descobrir o
        // óbvio — o dado já está na mão
        var w = await BuildAsync();
        await ScEnviadaAsync(w);
        w.Db.StageSlas.Add(new StageSla { Stage = "SOLICITACAO", MaxDays = 1 });
        await w.Db.SaveChangesAsync();
        w.Relogio.Agora = Dia1.AddDays(6);
        await w.Avisos.MarcarTudoLidoAsync(w.Gestor.Id);   // limpa o aviso 1

        var novos = await new EscalonamentoDoPrazoService(w.Db, w.Relogio).AvaliarAsync();

        Assert.Equal(1, novos);
        var aviso = (await w.Avisos.DaPessoaAsync(w.Gestor.Id, somenteNaoLidos: true)).Single();
        Assert.Equal(AvisoKinds.PrazoEstourado, aviso.Kind);
        Assert.Contains("Triagem", aviso.Body);     // de quem se espera
        Assert.Contains("prazo de 1", aviso.Body);  // contra que prazo
    }

    [Fact]
    public async Task O_mesmo_atraso_nao_vira_um_aviso_novo_a_cada_visita()
    {
        // sem isso, o gestor teria trinta linhas do mesmo problema — e repetir não faz o
        // atraso ser atendido, faz a caixa ser ignorada
        var w = await BuildAsync();
        await ScEnviadaAsync(w);
        w.Db.StageSlas.Add(new StageSla { Stage = "SOLICITACAO", MaxDays = 1 });
        await w.Db.SaveChangesAsync();
        w.Relogio.Agora = Dia1.AddDays(6);
        var escalonamento = new EscalonamentoDoPrazoService(w.Db, w.Relogio);

        await escalonamento.AvaliarAsync();
        var segunda = await escalonamento.AvaliarAsync();
        w.Relogio.Agora = Dia1.AddDays(20);
        var terceira = await escalonamento.AvaliarAsync();

        Assert.Equal(0, segunda);
        Assert.Equal(0, terceira);
        Assert.Single((await w.Avisos.DaPessoaAsync(w.Gestor.Id))
            .Where(n => n.Kind == AvisoKinds.PrazoEstourado));
    }

    [Fact]
    public async Task Dentro_do_prazo_nao_escala()
    {
        var w = await BuildAsync();
        await ScEnviadaAsync(w);
        w.Db.StageSlas.Add(new StageSla { Stage = "SOLICITACAO", MaxDays = 30 });
        await w.Db.SaveChangesAsync();
        w.Relogio.Agora = Dia1.AddDays(2);

        Assert.Equal(0, await new EscalonamentoDoPrazoService(w.Db, w.Relogio).AvaliarAsync());
    }

    [Fact]
    public async Task Uma_SC_de_varios_itens_estourados_e_um_aviso_so()
    {
        // o gestor age sobre a solicitação; cinco linhas iguais só fariam a caixa render menos
        var w = await BuildAsync();
        var (pr, _) = await w.Prs.CreateAsync(w.Ana, "Reposição", "CC-01", "NORMAL", null,
            [new ItemInput("Martelete", 1, "UN", 100, null),
             new ItemInput("Pé de cabra", 1, "UN", 70, null),
             new ItemInput("Luva", 1, "PAR", 20, null)]);
        await w.Prs.SubmitAsync(w.Ana, pr!.Id);
        w.Db.StageSlas.Add(new StageSla { Stage = "SOLICITACAO", MaxDays = 1 });
        await w.Db.SaveChangesAsync();
        w.Relogio.Agora = Dia1.AddDays(6);

        var novos = await new EscalonamentoDoPrazoService(w.Db, w.Relogio).AvaliarAsync();

        Assert.Equal(1, novos);
    }
}
