using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O comprador segue com parte da SC e deixa o resto pendente nela. A cotação leva só os itens
/// marcados; o que ficou continua na etapa de Solicitação — na lista, nos KPIs e na parede da
/// sala, que perguntam à mesma <see cref="AndamentoDosItens"/>. Resolver pela SC mostrava o item
/// deixado para trás "em cotação" num processo onde ele não estava.
/// </summary>
public class SeguirComParteDaScTests
{
    private sealed class Relogio : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class Numeros : IPrNumberGenerator
    {
        private int _n;
        public Task<string> NextAsync(CancellationToken ct = default) => Task.FromResult($"PR-2026-{++_n:000000}");
    }

    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana Solicitante", Roles.Requester);
    private static readonly Actor Carla = new(Guid.NewGuid(), "Carla Compradora", Roles.PurchasingOfficer);

    private sealed record Mundo(AppDbContext Db, TorreDeControleService Torre, QuotationService Rfq, PurchaseRequisition Sc);

    private static async Task<Mundo> MontarAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var relogio = new Relogio();
        var prs = new RequisitionService(db, new Numeros(), new CatalogService(db, relogio), relogio);
        var (sc, erro) = await prs.CreateAsync(Ana, "Material do escritório", "CC-01", "NORMAL", null,
            [new ItemInput("Caneta azul", 10, "UN", 2, null), new ItemInput("Grampeador", 2, "UN", 35, null)]);
        Assert.Null(erro);
        Assert.Null((await prs.SubmitAsync(Ana, sc!.Id)).error);
        return new(db, new TorreDeControleService(db, relogio), new QuotationService(db, relogio),
            await db.Requisitions.Include(r => r.Items).SingleAsync(r => r.Id == sc.Id));
    }

    [Fact]
    public async Task A_cotacao_leva_so_o_item_marcado_e_o_outro_fica_na_solicitacao()
    {
        var m = await MontarAsync();
        var caneta = m.Sc.Items.Single(i => i.Description == "Caneta azul");
        var (q, erro) = await m.Rfq.CreateFromItemsAsync(Carla, [caneta.Id], QuotationKind.Purchase, null, null);
        Assert.Null(erro);
        Assert.Equal("Caneta azul", Assert.Single(q!.Items).Description);

        var pagina = await m.Torre.ConsultarAsync(new FiltroTorre());
        var linhaCaneta = pagina.Items.Single(l => l.Description == "Caneta azul");
        var linhaGrampeador = pagina.Items.Single(l => l.Description == "Grampeador");
        Assert.Equal("COTACAO", linhaCaneta.Stage);
        Assert.Equal(q.Id, linhaCaneta.QuotationId);
        // o que ficou para trás não aparece no processo do outro: está pendente, na mesma SC
        Assert.Equal("SOLICITACAO", linhaGrampeador.Stage);
        Assert.Null(linhaGrampeador.QuotationId);
        Assert.Equal(m.Sc.Id, linhaGrampeador.RequisitionId);

        // o card conta cada item na etapa em que ele está, e o filtro abre a mesma lista
        Assert.Equal(1, pagina.Kpis.EmCotacao);
        Assert.Equal(1, pagina.Kpis.Novos);
        var soSolicitacao = await m.Torre.ConsultarAsync(new FiltroTorre(Stage: "SOLICITACAO"));
        Assert.Equal("Grampeador", Assert.Single(soSolicitacao.Items).Description);

        // a parede da sala diz o mesmo que a mesa
        var cockpit = await m.Torre.CockpitAsync();
        Assert.Equal(1, cockpit.Pipeline.Single(n => n.Etapa == "SOLICITACAO").Quantidade);
        Assert.Equal(1, cockpit.Pipeline.Single(n => n.Etapa == "COTACAO").Quantidade);
    }

    [Fact]
    public async Task O_item_que_ficou_pode_ser_cotado_depois_num_processo_proprio()
    {
        var m = await MontarAsync();
        var caneta = m.Sc.Items.Single(i => i.Description == "Caneta azul");
        var grampeador = m.Sc.Items.Single(i => i.Description == "Grampeador");
        var (primeiro, _) = await m.Rfq.CreateFromItemsAsync(Carla, [caneta.Id], QuotationKind.Purchase, null, null);
        var (segundo, erro) = await m.Rfq.CreateFromItemsAsync(Carla, [grampeador.Id], QuotationKind.Purchase, null, null);
        Assert.Null(erro);

        var pagina = await m.Torre.ConsultarAsync(new FiltroTorre());
        Assert.Equal(primeiro!.Id, pagina.Items.Single(l => l.Description == "Caneta azul").QuotationId);
        Assert.Equal(segundo!.Id, pagina.Items.Single(l => l.Description == "Grampeador").QuotationId);
        // o item que já está num processo não entra num segundo
        var (_, repetido) = await m.Rfq.CreateFromItemsAsync(Carla, [caneta.Id], QuotationKind.Purchase, null, null);
        Assert.Equal("RFQ-ERR-062", repetido!.Code);
    }

    [Fact]
    public async Task Processo_antigo_da_SC_inteira_continua_valendo_para_todos_os_itens()
    {
        // antes do rastreio por item, a cotação nascia da SC sem dizer de qual item veio cada
        // linha: é o que ela sempre significou, e continua valendo para os dois
        var m = await MontarAsync();
        var (q, erro) = await m.Rfq.CreateFromPrAsync(Carla, m.Sc.Id, QuotationKind.Purchase, null, null);
        Assert.Null(erro);
        foreach (var i in q!.Items) i.SourcePrItemId = null;
        await m.Db.SaveChangesAsync();

        var pagina = await m.Torre.ConsultarAsync(new FiltroTorre());
        Assert.All(pagina.Items, l => Assert.Equal("COTACAO", l.Stage));
        Assert.Equal(2, pagina.Kpis.EmCotacao);
    }
}
