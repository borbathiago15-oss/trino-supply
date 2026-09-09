using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// A régua do score, agora da empresa e não do código.
///
/// O que estes testes protegem é o que faria a régua mentir: peso que não fecha 100 (a tela
/// diria 40% e a conta usaria 25%), régua inteira zerada (score sem critério nenhum) e a
/// régua nova sem efeito no cálculo — que seria configurar no vazio.
/// </summary>
public class ScoreWeightsServiceTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static ScoreWeightsService Build() => new(
        new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
        TimeProvider.System);

    private static Actor Admin => new(Guid.NewGuid(), "Thiago", Roles.SystemAdministrator);
    private static Actor Comprador => new(Guid.NewGuid(), "Carla", Roles.PurchasingOfficer);

    [Fact]
    public async Task Sem_nada_gravado_vale_a_regua_de_fabrica()
    {
        // devolver nulo faria cada tela inventar peso próprio; o padrão é o que o código já usava
        var atuais = await Build().AtuaisAsync();

        Assert.Equal(40, atuais.Price);
        Assert.Equal(100, atuais.Price + atuais.Delivery + atuais.Payment + atuais.Otif + atuais.Risk);
    }

    [Fact]
    public async Task O_administrador_troca_a_regua_e_ela_passa_a_valer()
    {
        var svc = Build();

        var (salvos, erro) = await svc.SalvarAsync(Admin, new(60, 30, 0, 10, 0));

        Assert.Null(erro);
        Assert.Equal(60, salvos!.Price);
        Assert.Equal("Thiago", salvos.UpdatedByLabel);
        Assert.Equal(60, (await svc.AtuaisAsync()).Price);
    }

    [Fact]
    public async Task Peso_que_nao_soma_cem_e_recusado()
    {
        // a tela publica "Preço 40% · Entrega 20% · …" como repartição de um todo:
        // soma diferente faria a explicação mentir sobre a própria conta
        var (_, erro) = await Build().SalvarAsync(Admin, new(50, 30, 10, 20, 10));

        Assert.Equal("SCR-ERR-010", erro!.Code);
        Assert.Contains("120", erro.Message);
    }

    [Fact]
    public async Task Regua_inteira_zerada_cai_na_mesma_regra_da_soma()
    {
        // não precisa de erro próprio: soma zero já não é 100, e um código a mais para o
        // mesmo caso seria regra que nunca dispara
        var (_, erro) = await Build().SalvarAsync(Admin, new(0, 0, 0, 0, 0));

        Assert.Equal("SCR-ERR-010", erro!.Code);
    }

    [Fact]
    public async Task Peso_fora_da_faixa_e_recusado_antes_da_soma()
    {
        var (_, erro) = await Build().SalvarAsync(Admin, new(-10, 60, 20, 20, 10));

        Assert.Equal("SCR-ERR-012", erro!.Code);
    }

    [Fact]
    public async Task Comprador_nao_mexe_na_regua_da_empresa()
    {
        // trocar o peso muda a ordem de toda comparação da empresa de uma vez
        var (_, erro) = await Build().SalvarAsync(Comprador, new(40, 20, 10, 20, 10));

        Assert.Equal("SCR-ERR-900", erro!.Code);
        Assert.False(ScoreWeightsService.CanEdit(Roles.PurchasingOfficer));
        Assert.True(ScoreWeightsService.CanEdit(Roles.SystemAdministrator));
    }

    [Fact]
    public void A_regua_gravada_e_a_que_entra_no_calculo()
    {
        // com preço valendo tudo, o mais barato vence mesmo tendo o pior OTIF
        var soPreco = new ScoreWeights { Price = 100, Delivery = 0, Payment = 0, Otif = 0, Risk = 0 };
        var soOtif = new ScoreWeights { Price = 0, Delivery = 0, Payment = 0, Otif = 100, Risk = 0 };
        ScoreInput[] propostas =
        [
            new(A, "Barato ruim", 1000m, null, null, 0, null),
            new(B, "Caro pontual", 2000m, null, null, 100, null),
        ];

        Assert.Equal(A, MultiCriteriaScore.Compute(propostas, soPreco.Criterios())[0].SupplierId);
        Assert.Equal(B, MultiCriteriaScore.Compute(propostas, soOtif.Criterios())[0].SupplierId);
    }

    [Fact]
    public void Criterio_desligado_nao_entra_no_denominador()
    {
        // peso 0 é "aqui isto não importa": mantê-lo na conta derrubaria o score por causa
        // de algo que a empresa dispensou
        var semOtif = new ScoreWeights { Price = 100, Delivery = 0, Payment = 0, Otif = 0, Risk = 0 };

        var linha = MultiCriteriaScore.Compute(
            [new(A, "Único", 1000m, null, null, 0, null)], semOtif.Criterios()).Single();

        Assert.Equal(100, linha.Score);   // preço 100, e o OTIF zerado não puxa para baixo
        Assert.Equal(0, linha.OtifPct);   // o componente continua visível: desligado não é escondido
    }

    [Fact]
    public void Os_rotulos_e_as_explicacoes_continuam_sendo_do_codigo()
    {
        // a empresa define quanto vale cada critério, não o que cada critério significa
        var regua = new ScoreWeights { Price = 100, Delivery = 0, Payment = 0, Otif = 0, Risk = 0 }.Criterios();

        Assert.Equal(MultiCriteriaScore.Padrao.Select(c => c.Code), regua.Select(c => c.Code));
        Assert.Equal(MultiCriteriaScore.Padrao.Single(c => c.Code == "price").Label,
            regua.Single(c => c.Code == "price").Label);
        Assert.Equal(1.0, regua.Sum(c => c.Weight), 6);
    }
}
