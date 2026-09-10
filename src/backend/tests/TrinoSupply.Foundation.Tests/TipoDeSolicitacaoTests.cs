using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O cadastro de tipos de solicitação.
///
/// O campo já existia na SC como texto livre que nenhuma tela preenchia. Livre, ele daria
/// "EPI", "epi" e "E.P.I." como três tipos que nunca somam — e é sobre esse valor que o prazo
/// por tipo é escolhido. O que estes testes protegem é o código ser identidade de verdade:
/// único, estável e comparável.
/// </summary>
public class TipoDeSolicitacaoTests
{
    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    private static readonly Actor Gestor = new(Guid.NewGuid(), "Marina", Roles.SupplyManager);
    private static readonly Actor Solicitante = new(Guid.NewGuid(), "Ana", Roles.Requester);

    private static TipoDeSolicitacaoService Build() => new(
        new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
        new RelogioFixo(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task O_codigo_e_gravado_em_caixa_alta_e_sem_espacos()
    {
        // é assim que ele fica na SC, e é assim que o prazo por tipo o procura
        var (tipo, erro) = await Build().CriarAsync(Gestor, "  emergencial ", "Emergencial", "Parada de linha");

        Assert.Null(erro);
        Assert.Equal("EMERGENCIAL", tipo!.Code);
        Assert.Equal("Emergencial", tipo.Name);
        Assert.True(tipo.Active);
    }

    [Fact]
    public async Task Codigo_repetido_e_recusado_mesmo_em_caixa_diferente()
    {
        // "EPI" e "epi" como dois tipos é exatamente o que o cadastro existe para impedir
        var svc = Build();
        await svc.CriarAsync(Gestor, "EPI", "EPI", null);

        var (_, erro) = await svc.CriarAsync(Gestor, "epi", "Equipamento de proteção", null);

        Assert.Equal("TP-ERR-011", erro!.Code);
    }

    [Fact]
    public async Task Codigo_e_nome_curtos_demais_sao_recusados()
    {
        var svc = Build();

        var (_, semCodigo) = await svc.CriarAsync(Gestor, "", "Emergencial", null);
        var (_, semNome) = await svc.CriarAsync(Gestor, "EMERG", "", null);

        Assert.Equal("TP-ERR-010", semCodigo!.Code);
        Assert.Equal("TP-ERR-010", semNome!.Code);
    }

    [Fact]
    public async Task O_nome_se_corrige_e_o_codigo_nao_muda()
    {
        // SCs já criadas carregam o código; trocá-lo faria as antigas apontarem para um
        // tipo que deixou de existir. O nome é rótulo, e rótulo se conserta à vontade
        var svc = Build();
        var (tipo, _) = await svc.CriarAsync(Gestor, "EMERGENCIAL", "Emergencia", null);

        var (depois, erro) = await svc.AtualizarAsync(Gestor, tipo!.Id, "Emergencial", "Parada de linha", null);

        Assert.Null(erro);
        Assert.Equal("Emergencial", depois!.Name);
        Assert.Equal("EMERGENCIAL", depois.Code);   // segue o mesmo
    }

    [Fact]
    public async Task Tipo_inativado_sai_da_lista_e_continua_existindo()
    {
        // apagar quebraria as SCs que já o escolheram; inativar tira das novas e mantém
        // legível o que já foi criado
        var svc = Build();
        var (tipo, _) = await svc.CriarAsync(Gestor, "ANTIGO", "Antigo", null);
        await svc.AtualizarAsync(Gestor, tipo!.Id, null, null, false);

        Assert.Empty(await svc.ListarAsync());
        Assert.Single(await svc.ListarAsync(incluirInativos: true));
    }

    [Fact]
    public async Task Solicitante_nao_mantem_o_cadastro()
    {
        var svc = Build();

        var (_, erro) = await svc.CriarAsync(Solicitante, "EMERGENCIAL", "Emergencial", null);

        Assert.Equal("TP-ERR-900", erro!.Code);
        Assert.False(TipoDeSolicitacaoService.CanMaintain(Roles.Requester));
        Assert.True(TipoDeSolicitacaoService.CanMaintain(Roles.SupplyManager));
        Assert.True(TipoDeSolicitacaoService.CanMaintain(Roles.SystemAdministrator));
    }

    [Fact]
    public async Task Atualizar_tipo_que_nao_existe_devolve_404()
    {
        var (_, erro) = await Build().AtualizarAsync(Gestor, Guid.NewGuid(), "Novo nome", null, null);

        Assert.Equal("TP-ERR-404", erro!.Code);
    }
}
