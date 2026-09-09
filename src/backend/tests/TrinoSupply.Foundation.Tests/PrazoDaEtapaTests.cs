using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O prazo de cada etapa: o que transforma "parado há 6 dias" em "passou do prazo".
///
/// O que estes testes protegem é o que faria o veredito ser injusto: medir contra a criação
/// da SC (e cobrar da aprovação o tempo que o item passou em cotação), avisar só no dia do
/// estouro (quando já não dá para agir) e um prazo desligado virando estouro automático.
/// </summary>
public class PrazoDaEtapaTests
{
    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    private static readonly Actor Admin = new(Guid.NewGuid(), "Thiago", Roles.SystemAdministrator);
    private static readonly Actor Comprador = new(Guid.NewGuid(), "Carla", Roles.PurchasingOfficer);

    private static PrazoDaEtapaService Build() => new(
        new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
        new RelogioFixo(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task Sem_nada_configurado_valem_os_prazos_de_fabrica()
    {
        // a tela nasce com números que fazem sentido, e não com zeros que desligam tudo
        var atuais = await Build().AtuaisAsync();

        Assert.Equal(PrazoDaEtapaService.Padrao.Count, atuais.Count);
        Assert.Equal(7, atuais.Single(s => s.Stage == "COTACAO").MaxDays);
        Assert.DoesNotContain(atuais, s => s.Stage == "ENCERRADO");   // não há prazo para o que terminou
    }

    [Fact]
    public async Task O_administrador_troca_o_prazo_de_uma_etapa_sem_mexer_nas_outras()
    {
        var svc = Build();

        var (salvos, erro) = await svc.SalvarAsync(Admin, new Dictionary<string, int> { ["APROVACAO"] = 10 });

        Assert.Null(erro);
        Assert.Equal(10, salvos!.Single(s => s.Stage == "APROVACAO").MaxDays);
        Assert.Equal(7, salvos.Single(s => s.Stage == "COTACAO").MaxDays);
        Assert.Equal("Thiago", salvos.Single(s => s.Stage == "APROVACAO").UpdatedByLabel);
    }

    [Fact]
    public async Task Comprador_nao_define_prazo_da_empresa()
    {
        var (_, erro) = await Build().SalvarAsync(Comprador, new Dictionary<string, int> { ["COTACAO"] = 3 });

        Assert.Equal("SLA-ERR-900", erro!.Code);
        Assert.False(PrazoDaEtapaService.CanEdit(Roles.PurchasingOfficer));
    }

    [Fact]
    public async Task Prazo_fora_da_faixa_e_etapa_inventada_sao_recusados()
    {
        var svc = Build();

        var (_, faixa) = await svc.SalvarAsync(Admin, new Dictionary<string, int> { ["COTACAO"] = 400 });
        var (_, etapa) = await svc.SalvarAsync(Admin, new Dictionary<string, int> { ["INVENTADA"] = 3 });

        Assert.Equal("SLA-ERR-010", faixa!.Code);
        Assert.Equal("SLA-ERR-011", etapa!.Code);
    }

    [Fact]
    public void O_veredito_sai_do_prazo_da_etapa_e_do_relogio_da_espera()
    {
        var prazos = new Dictionary<string, int> { ["APROVACAO"] = 5 };

        Assert.Equal("OK", PrazoDaEtapaService.Avaliar("APROVACAO", 2, prazos).Status);
        Assert.Equal("ATENCAO", PrazoDaEtapaService.Avaliar("APROVACAO", 4, prazos).Status);
        Assert.Equal("ESTOURADO", PrazoDaEtapaService.Avaliar("APROVACAO", 6, prazos).Status);
        Assert.Equal(5, PrazoDaEtapaService.Avaliar("APROVACAO", 6, prazos).MaxDays);
    }

    [Fact]
    public void No_dia_do_prazo_ainda_nao_estourou_e_no_seguinte_sim()
    {
        // "até o quinto dia" precisa incluir o quinto: estourar no próprio prazo tiraria
        // do comprador o dia que ele tinha
        var prazos = new Dictionary<string, int> { ["COTACAO"] = 5 };

        Assert.False(PrazoDaEtapaService.Avaliar("COTACAO", 5, prazos).Breached);
        Assert.True(PrazoDaEtapaService.Avaliar("COTACAO", 6, prazos).Breached);
    }

    [Fact]
    public void A_atencao_chega_antes_do_estouro_para_dar_tempo_de_agir()
    {
        // avisar no dia do vencimento é avisar tarde demais para mudar alguma coisa
        var prazos = new Dictionary<string, int> { ["COTACAO"] = 10 };

        Assert.Equal("OK", PrazoDaEtapaService.Avaliar("COTACAO", 7, prazos).Status);
        Assert.Equal("ATENCAO", PrazoDaEtapaService.Avaliar("COTACAO", 8, prazos).Status);
        Assert.Equal("ATENCAO", PrazoDaEtapaService.Avaliar("COTACAO", 10, prazos).Status);
        Assert.Equal("ESTOURADO", PrazoDaEtapaService.Avaliar("COTACAO", 11, prazos).Status);
    }

    [Fact]
    public void Prazo_zero_desliga_a_etapa_em_vez_de_estourar_sempre()
    {
        // zero é "aqui não cobramos tempo"; tratá-lo como teto faria toda linha nascer
        // vermelha e ensinaria o comprador a ignorar a cor
        var desligado = PrazoDaEtapaService.Avaliar("COTACAO", 90, new Dictionary<string, int> { ["COTACAO"] = 0 });

        Assert.Null(desligado.Status);
        Assert.False(desligado.Breached);
    }

    [Fact]
    public void Etapa_sem_prazo_e_espera_sem_data_nao_recebem_veredito()
    {
        // sem base de comparação, um veredito seria invenção
        Assert.Null(PrazoDaEtapaService.Avaliar("ENCERRADO", 30, new Dictionary<string, int>()).Status);

        var semData = PrazoDaEtapaService.Avaliar("COTACAO", null, new Dictionary<string, int> { ["COTACAO"] = 5 });
        Assert.Null(semData.Status);
        Assert.Equal(5, semData.MaxDays);   // o prazo aparece mesmo sem veredito: a régua é pública
    }
}
