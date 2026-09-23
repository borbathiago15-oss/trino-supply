using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Suporte;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O chamado de suporte. O que se trava aqui: abrir não depende de permissão, a situação diz
/// de quem é a vez, o suporte não resolve em silêncio, e o chamado alheio não existe para quem
/// não atende.
/// </summary>
public class ChamadoDeSuporteTests
{
    private sealed class Relogio(DateTimeOffset agora) : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = agora;
        public override DateTimeOffset GetUtcNow() => Agora;
    }

    private static readonly DateTimeOffset Inicio = new(2026, 9, 23, 9, 0, 0, TimeSpan.Zero);

    private sealed record Cenario(AppDbContext Db, ChamadoService Svc, Relogio Relogio,
        QuemChama Solicitante, QuemChama Outro, QuemChama Admin, QuemChama Atendente);

    private static async Task<Cenario> Montar()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var admin = new User { Name = "Ana Admin", Email = "ana@t.com", Role = Roles.SystemAdministrator };
        var atendente = new User { Name = "Beto Suporte", Email = "beto@t.com", Role = Roles.PurchasingOfficer,
            Modules = "COMPRAS,SUPORTE" };
        var solicitante = new User { Name = "Caio Solicitante", Email = "caio@t.com", Role = Roles.Requester };
        var outro = new User { Name = "Dora", Email = "dora@t.com", Role = Roles.Requester };
        // inativo com o módulo: não atende mais, e não recebe aviso
        var inativo = new User { Name = "Eva", Email = "eva@t.com", Role = Roles.Requester, Modules = "SUPORTE", Active = false };
        db.Users.AddRange(admin, atendente, solicitante, outro, inativo);
        await db.SaveChangesAsync();

        var relogio = new Relogio(Inicio);
        var svc = new ChamadoService(db, relogio, new AvisoDoUsuarioService(db, relogio));
        return new(db, svc, relogio,
            new(solicitante.Id, solicitante.Name, false), new(outro.Id, outro.Name, false),
            new(admin.Id, admin.Name, true), new(atendente.Id, atendente.Name, true));
    }

    private static AbrirChamado Pedido(string assunto = "Não consigo aprovar",
        string descricao = "O botão de aprovar não aparece na Central.") =>
        new("DUVIDA", assunto, descricao, "/aprovacoes", "Central de Aprovação", "Chrome 1920x1080");

    [Fact]
    public async Task Abrir_nao_pede_modulo_e_grava_a_tela_e_a_descricao_como_primeira_mensagem()
    {
        var c = await Montar();
        var (chamado, erro) = await c.Svc.AbrirAsync(c.Solicitante, Pedido());

        Assert.Null(erro);
        Assert.Equal("CH-2026-000001", chamado!.Number);
        Assert.Equal(SituacaoDoChamado.AguardandoSuporte, chamado.Status);
        Assert.Equal("/aprovacoes", chamado.Screen);
        Assert.Equal("Central de Aprovação", chamado.ScreenLabel);
        var mensagem = Assert.Single(await c.Db.SupportTicketMessages.ToListAsync());
        Assert.Equal("O botão de aprovar não aparece na Central.", mensagem.Text);
        Assert.False(mensagem.FromSupport);
    }

    [Fact]
    public async Task A_numeracao_segue_no_ano()
    {
        var c = await Montar();
        await c.Svc.AbrirAsync(c.Solicitante, Pedido());
        var (segundo, _) = await c.Svc.AbrirAsync(c.Outro, Pedido());
        Assert.Equal("CH-2026-000002", segundo!.Number);
    }

    [Theory]
    [InlineData("OUTRA", "Assunto certo", "Descrição suficiente", "CH-ERR-012")]
    [InlineData("ERRO", "Ajud", "Descrição suficiente", "CH-ERR-010")]
    [InlineData("ERRO", "Assunto certo", "curta", "CH-ERR-011")]
    public async Task O_pedido_incompleto_e_recusado_com_o_que_falta(
        string categoria, string assunto, string descricao, string codigo)
    {
        var c = await Montar();
        var (chamado, erro) = await c.Svc.AbrirAsync(c.Solicitante,
            new(categoria, assunto, descricao, "/painel", "Dashboard", null));
        Assert.Null(chamado);
        Assert.Equal(codigo, erro!.Code);
        Assert.Empty(await c.Db.SupportTickets.ToListAsync());
    }

    [Fact]
    public async Task Cada_atendente_ativo_recebe_o_seu_aviso_e_quem_nao_atende_nao()
    {
        var c = await Montar();
        await c.Svc.AbrirAsync(c.Solicitante, Pedido());

        var avisados = await c.Db.UserNotices.Where(n => n.Kind == ChamadoService.AvisoAberto)
            .Select(n => n.UserId).ToListAsync();
        Assert.Equal(new[] { c.Admin.Id, c.Atendente.Id }.OrderBy(x => x), avisados.OrderBy(x => x));
    }

    [Fact]
    public void O_modulo_SUPORTE_nao_entra_em_padrao_de_papel_nenhum()
    {
        // atender é função que se dá a alguém: nenhum cargo o recebe por nascer com ele
        var papeis = new[] { Roles.Requester, Roles.Approver, Roles.PurchasingOfficer, Roles.WarehouseOperator,
            Roles.WarehouseSupervisor, Roles.SupplyManager, Roles.Director, Roles.Auditor };
        Assert.All(papeis, p => Assert.DoesNotContain(AppModules.Suporte, AppModules.DefaultsFor(p)));
        Assert.Contains(AppModules.Suporte, AppModules.EffectiveFor(new User { Role = Roles.SystemAdministrator }));
    }

    [Fact]
    public async Task Quem_nao_atende_ve_so_os_seus_e_o_alheio_nao_existe()
    {
        var c = await Montar();
        var (meu, _) = await c.Svc.AbrirAsync(c.Solicitante, Pedido());
        await c.Svc.AbrirAsync(c.Outro, Pedido("Outro assunto aqui"));

        Assert.Single(await c.Svc.ListarAsync(c.Solicitante, fila: false, situacao: null));
        // pedir a fila sem atender devolve só os próprios — nunca a de todo mundo
        Assert.Single(await c.Svc.ListarAsync(c.Solicitante, fila: true, situacao: null));
        Assert.Null(await c.Svc.AbrirParaLerAsync(c.Outro, meu!.Id));
        var (_, _, erro) = await c.Svc.ResponderAsync(c.Outro, meu.Id, "Intrometendo", false);
        Assert.Equal("CH-ERR-404", erro!.Code);

        Assert.Equal(2, (await c.Svc.ListarAsync(c.Atendente, fila: true, situacao: null)).Count);
        Assert.NotNull(await c.Svc.AbrirParaLerAsync(c.Atendente, meu.Id));
    }

    [Fact]
    public async Task A_resposta_do_suporte_passa_a_vez_e_assume_o_chamado()
    {
        var c = await Montar();
        var (chamado, _) = await c.Svc.AbrirAsync(c.Solicitante, Pedido());

        var (mensagem, depois, erro) = await c.Svc.ResponderAsync(c.Atendente, chamado!.Id,
            "A Central só mostra o que o seu centro permite aprovar.", false);

        Assert.Null(erro);
        Assert.True(mensagem!.FromSupport);
        Assert.Equal(SituacaoDoChamado.AguardandoUsuario, depois!.Status);
        Assert.Equal(c.Atendente.Id, depois.AssignedToId);
        Assert.Contains(await c.Db.UserNotices.ToListAsync(),
            n => n.UserId == c.Solicitante.Id && n.Kind == ChamadoService.AvisoResposta);
    }

    [Fact]
    public async Task A_resposta_de_quem_pediu_volta_a_vez_ao_suporte_e_avisa_so_quem_assumiu()
    {
        var c = await Montar();
        var (chamado, _) = await c.Svc.AbrirAsync(c.Solicitante, Pedido());
        await c.Svc.ResponderAsync(c.Atendente, chamado!.Id, "Qual é o centro de custo da SC?", false);

        var (_, depois, _) = await c.Svc.ResponderAsync(c.Solicitante, chamado.Id, "É o CC-0101.", false);

        Assert.Equal(SituacaoDoChamado.AguardandoSuporte, depois!.Status);
        var respostas = await c.Db.UserNotices
            .Where(n => n.Kind == ChamadoService.AvisoResposta && n.UserId != c.Solicitante.Id).ToListAsync();
        Assert.Equal(c.Atendente.Id, Assert.Single(respostas).UserId);
    }

    [Fact]
    public async Task O_suporte_nao_resolve_em_silencio()
    {
        var c = await Montar();
        var (chamado, _) = await c.Svc.AbrirAsync(c.Solicitante, Pedido());

        var (_, _, erro) = await c.Svc.ResponderAsync(c.Atendente, chamado!.Id, "", true);
        Assert.Equal("CH-ERR-021", erro!.Code);

        var (_, depois, ok) = await c.Svc.ResponderAsync(c.Atendente, chamado.Id,
            "Liberei o módulo de aprovação no seu cadastro.", true);
        Assert.Null(ok);
        Assert.Equal(SituacaoDoChamado.Resolvido, depois!.Status);
        Assert.Equal("Beto Suporte", depois.ResolvedByLabel);
    }

    [Fact]
    public async Task Quem_abriu_encerra_sem_texto()
    {
        var c = await Montar();
        var (chamado, _) = await c.Svc.AbrirAsync(c.Solicitante, Pedido());
        var (mensagem, depois, erro) = await c.Svc.ResponderAsync(c.Solicitante, chamado!.Id, null, true);

        Assert.Null(erro);
        Assert.Null(mensagem);
        Assert.Equal(SituacaoDoChamado.Resolvido, depois!.Status);
        var (_, _, deNovo) = await c.Svc.ResponderAsync(c.Solicitante, chamado.Id, null, true);
        Assert.Equal("CH-ERR-022", deNovo!.Code);
    }

    [Fact]
    public async Task Responder_o_resolvido_reabre_e_apaga_o_veredito()
    {
        var c = await Montar();
        var (chamado, _) = await c.Svc.AbrirAsync(c.Solicitante, Pedido());
        await c.Svc.ResponderAsync(c.Atendente, chamado!.Id, "Liberei o módulo de aprovação.", true);

        var (_, depois, _) = await c.Svc.ResponderAsync(c.Solicitante, chamado.Id, "Continua sem aparecer.", false);

        Assert.Equal(SituacaoDoChamado.AguardandoSuporte, depois!.Status);
        Assert.Null(depois.ResolvedAt);
        Assert.Null(depois.ResolvedByLabel);
    }

    [Fact]
    public async Task Mensagem_vazia_e_recusada()
    {
        var c = await Montar();
        var (chamado, _) = await c.Svc.AbrirAsync(c.Solicitante, Pedido());
        var (_, _, erro) = await c.Svc.ResponderAsync(c.Solicitante, chamado!.Id, "   ", false);
        Assert.Equal("CH-ERR-020", erro!.Code);
    }

    [Fact]
    public async Task No_proprio_chamado_quem_atende_fala_como_quem_pediu_e_nao_se_avisa()
    {
        var c = await Montar();
        var (chamado, _) = await c.Svc.AbrirAsync(c.Atendente, Pedido());
        Assert.DoesNotContain(await c.Db.UserNotices.ToListAsync(), n => n.UserId == c.Atendente.Id);

        var (mensagem, depois, _) = await c.Svc.ResponderAsync(c.Atendente, chamado!.Id, "Mais um detalhe.", false);
        Assert.False(mensagem!.FromSupport);
        Assert.Equal(SituacaoDoChamado.AguardandoSuporte, depois!.Status);
        Assert.Null(depois.AssignedToId);
    }

    [Fact]
    public async Task O_resumo_diz_o_que_e_a_minha_vez_e_a_fila_so_para_quem_atende()
    {
        var c = await Montar();
        var (a, _) = await c.Svc.AbrirAsync(c.Solicitante, Pedido());
        await c.Svc.AbrirAsync(c.Solicitante, Pedido("Segundo assunto"));
        await c.Svc.ResponderAsync(c.Atendente, a!.Id, "Pode mandar o print da tela?", false);

        Assert.Equal((1, (int?)null), await c.Svc.ResumoAsync(c.Solicitante));
        Assert.Equal((0, (int?)1), await c.Svc.ResumoAsync(c.Atendente));
    }

    [Fact]
    public async Task A_fila_de_quem_atende_nao_conta_o_chamado_que_ele_mesmo_abriu()
    {
        // no chamado dele, ele é quem pediu: "há trabalho esperando você" seria falso
        var c = await Montar();
        await c.Svc.AbrirAsync(c.Atendente, Pedido());
        Assert.Equal((0, (int?)0), await c.Svc.ResumoAsync(c.Atendente));
        Assert.Equal((0, (int?)1), await c.Svc.ResumoAsync(c.Admin));
    }

    [Fact]
    public async Task So_quem_escreveu_anexa_e_uma_vez_so()
    {
        var c = await Montar();
        var (chamado, _) = await c.Svc.AbrirAsync(c.Solicitante, Pedido());
        var primeira = (await c.Db.SupportTicketMessages.SingleAsync()).Id;

        var (_, deOutro) = await c.Svc.MensagemParaAnexarAsync(c.Atendente, chamado!.Id, primeira);
        Assert.Equal("CH-ERR-030", deOutro!.Code);
        var (_, alheio) = await c.Svc.MensagemParaAnexarAsync(c.Outro, chamado.Id, primeira);
        Assert.Equal("CH-ERR-404", alheio!.Code);

        var (mensagem, ok) = await c.Svc.MensagemParaAnexarAsync(c.Solicitante, chamado.Id, primeira);
        Assert.Null(ok);
        mensagem!.AttachmentId = Guid.NewGuid();
        await c.Db.SaveChangesAsync();
        var (_, segundo) = await c.Svc.MensagemParaAnexarAsync(c.Solicitante, chamado.Id, primeira);
        Assert.Equal("CH-ERR-031", segundo!.Code);
    }

    [Fact]
    public async Task O_download_do_anexo_segue_a_regua_da_leitura()
    {
        var c = await Montar();
        var (chamado, _) = await c.Svc.AbrirAsync(c.Solicitante, Pedido());
        Assert.True(await c.Svc.PodeVerAsync(c.Solicitante, chamado!.Id));
        Assert.True(await c.Svc.PodeVerAsync(c.Admin, chamado.Id));
        Assert.False(await c.Svc.PodeVerAsync(c.Outro, chamado.Id));
    }
}
