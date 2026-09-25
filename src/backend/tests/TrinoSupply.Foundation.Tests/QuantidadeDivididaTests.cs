using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// A quantidade de um item dividida entre fornecedores: mil botas em que setecentas vão com
/// um e trezentas com outro.
///
/// O que estes testes protegem é o dinheiro. Dividir a quantidade e não dividir o rateio
/// cobraria o mesmo frete duas vezes; deixar a soma fechar menos que o pedido deixaria parte
/// da solicitação sem comprar sem ninguém ter decidido isso; e a O.C. com a quantidade cotada
/// (em vez da adjudicada) mandaria ao fornecedor o dobro do que ele ganhou.
/// </summary>
public class QuantidadeDivididaTests
{
    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    private sealed class NumerosFalsos : IPrNumberGenerator
    {
        private int _proximo;
        public Task<string> NextAsync(CancellationToken ct = default) =>
            Task.FromResult($"PR-2026-{++_proximo:000000}");
    }

    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana", Roles.Requester);
    private static readonly Actor Bruno = new(Guid.NewGuid(), "Bruno", Roles.Approver);
    private static readonly Actor Carla = new(Guid.NewGuid(), "Carla", Roles.PurchasingOfficer);

    private sealed record Mundo(
        AppDbContext Db, QuotationService Rfq, Supplier Alfa, Supplier Beta, Quotation Q);

    /// <summary>Uma cotação de um item (1000 botas), com proposta dos dois fornecedores.</summary>
    private static async Task<Mundo> BuildAsync(decimal precoAlfa = 10m, decimal precoBeta = 12m,
        decimal freteAlfa = 500m, decimal freteBeta = 300m)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(new ItensDaCotacaoNoCatalogo()).Options);
        var relogio = new RelogioFixo(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        var rfq = new QuotationService(db, relogio);
        var sup = new SupplierService(db, relogio);
        var prs = new RequisitionService(db, new NumerosFalsos(), new CatalogService(db, relogio), relogio);

        var (alfa, _) = await sup.CreateAsync(Carla.Id, "Alfa LTDA", "Alfa", "12345678000190", null, "81 3333-1000");
        var (beta, _) = await sup.CreateAsync(Carla.Id, "Beta LTDA", "Beta", "98765432000110", null, "81 3333-2000");
        await sup.SetHomologationAsync(alfa!.Id, SupplierHomologation.Homologado);
        await sup.SetHomologationAsync(beta!.Id, SupplierHomologation.Homologado);

        var (pr, _) = await prs.CreateAsync(Ana, "EPI", "CC-01", "NORMAL", null,
            [new ItemInput("Bota de segurança", 1000, "PAR", 10, null)]);
        await prs.SubmitAsync(Ana, pr!.Id);
        await prs.ApproveAsync(Bruno, pr.Id, null);
        var (q, _) = await rfq.CreateFromPrAsync(Carla, pr.Id, QuotationKind.Purchase, null, null);

        await rfq.InviteSuppliersAsync(Carla, q!.Id, [alfa.Id, beta.Id]);
        var item = q.Items.Single().Id;
        await rfq.SubmitProposalAsync(q.Id, alfa.Id,
            new(10, "30 dias", freteAlfa, null, null, [new ProposalItemInput(item, precoAlfa, null)]),
            "PORTAL", "Alfa");
        await rfq.SubmitProposalAsync(q.Id, beta.Id,
            new(10, "30 dias", freteBeta, null, null, [new ProposalItemInput(item, precoBeta, null)]),
            "PORTAL", "Beta");
        await rfq.CloseForAnalysisAsync(Carla, q.Id);

        return new Mundo(db, rfq, alfa, beta, (await rfq.GetAsync(q.Id))!);
    }

    private static Guid PropostaDe(Quotation q, Guid fornecedor) =>
        q.Proposals.Single(p => p.SupplierId == fornecedor).Id;

    private static List<AwardInput> Dividir(Mundo w, decimal paraAlfa, decimal paraBeta)
    {
        var item = w.Q.Items.Single().Id;
        return
        [
            new AwardInput("", PropostaDe(w.Q, w.Alfa.Id), "Menor preco", "Melhor preco no volume", item, paraAlfa),
            new AwardInput("", PropostaDe(w.Q, w.Beta.Id), "Prazo", "Cobre o saldo no prazo", item, paraBeta),
        ];
    }

    [Fact]
    public async Task O_mesmo_item_vai_a_dois_fornecedores_quando_a_quantidade_se_divide()
    {
        var w = await BuildAsync();

        var (fechado, erro) = await w.Rfq.AwardByItemAsync(Carla, w.Q.Id, Dividir(w, 700, 300));

        Assert.Null(erro);
        Assert.Equal(2, fechado!.AwardList.Count);
        Assert.Equal(700, fechado.AwardList.Single(a => a.SupplierId == w.Alfa.Id).Quantity);
        Assert.Equal(300, fechado.AwardList.Single(a => a.SupplierId == w.Beta.Id).Quantity);
        Assert.True(fechado.IsSplitAward);
    }

    [Fact]
    public async Task O_valor_de_cada_fatia_sai_da_quantidade_que_ela_leva()
    {
        // Alfa: 700 × 10 = 7.000 em itens; Beta: 300 × 12 = 3.600
        var w = await BuildAsync();

        var (fechado, _) = await w.Rfq.AwardByItemAsync(Carla, w.Q.Id, Dividir(w, 700, 300));

        Assert.Equal(7000m, fechado!.AwardList.Single(a => a.SupplierId == w.Alfa.Id).ItemsValue);
        Assert.Equal(3600m, fechado.AwardList.Single(a => a.SupplierId == w.Beta.Id).ItemsValue);
    }

    [Fact]
    public async Task O_frete_e_rateado_pela_fatia_e_nao_cobrado_inteiro_dos_dois()
    {
        // Alfa cotou 1000 × 10 = 10.000 e frete 500. Levando 700, a fatia é 70% do que ele
        // cotou, e o frete acompanha: 350. Dar os 500 cheios a ele e outros 300 a Beta
        // faria a compra pagar frete que ninguém combinou
        var w = await BuildAsync(precoAlfa: 10m, freteAlfa: 500m);

        var (fechado, _) = await w.Rfq.AwardByItemAsync(Carla, w.Q.Id, Dividir(w, 700, 300));

        var deAlfa = fechado!.AwardList.Single(a => a.SupplierId == w.Alfa.Id);
        Assert.Equal(7350m, deAlfa.TotalValue);            // 7.000 + 350 de frete
        Assert.NotEqual(deAlfa.ItemsValue + 500m, deAlfa.TotalValue);
    }

    [Fact]
    public async Task Quem_leva_a_quantidade_inteira_continua_sem_rateio()
    {
        // o caminho de sempre não muda: sem divisão, o total é o da proposta, sem
        // arredondamento nenhum no meio
        var w = await BuildAsync();
        var item = w.Q.Items.Single().Id;

        var (fechado, erro) = await w.Rfq.AwardByItemAsync(Carla, w.Q.Id,
            [new AwardInput("", PropostaDe(w.Q, w.Alfa.Id), "Menor preco", "Levou tudo", item)]);

        Assert.Null(erro);
        var award = fechado!.AwardList.Single();
        Assert.Null(award.Quantity);                        // nulo = a quantidade inteira
        Assert.Equal(10500m, award.TotalValue);             // 1000×10 + 500 de frete cheio
    }

    [Fact]
    public async Task A_soma_precisa_fechar_a_quantidade_pedida()
    {
        var w = await BuildAsync();

        var (_, faltando) = await w.Rfq.AwardByItemAsync(Carla, w.Q.Id, Dividir(w, 700, 200));
        var (_, sobrando) = await w.Rfq.AwardByItemAsync(Carla, w.Q.Id, Dividir(w, 700, 500));

        Assert.Equal("RFQ-ERR-025", faltando!.Code);
        Assert.Contains("900", faltando.Message);
        Assert.Contains("1000", faltando.Message);
        Assert.Equal("RFQ-ERR-025", sobrando!.Code);
    }

    [Fact]
    public async Task Quantidade_zero_ou_negativa_e_recusada()
    {
        var w = await BuildAsync();

        var (_, zero) = await w.Rfq.AwardByItemAsync(Carla, w.Q.Id, Dividir(w, 1000, 0));
        var (_, negativa) = await w.Rfq.AwardByItemAsync(Carla, w.Q.Id, Dividir(w, 1100, -100));

        Assert.Equal("RFQ-ERR-025", zero!.Code);
        Assert.Equal("RFQ-ERR-025", negativa!.Code);
    }

    [Fact]
    public async Task Dividir_a_familia_por_quantidade_nao_faz_sentido_e_e_recusado()
    {
        // a família tem itens de unidades diferentes: "300 da família EPI" não quer dizer nada
        var w = await BuildAsync();

        var (_, erro) = await w.Rfq.AwardByItemAsync(Carla, w.Q.Id,
            [new AwardInput("EPI", PropostaDe(w.Q, w.Alfa.Id), null, "Justificativa", null, 300)]);

        Assert.Equal("RFQ-ERR-025", erro!.Code);
        Assert.Contains("unidades diferentes", erro.Message);
    }

    [Fact]
    public async Task Sem_quantidade_o_item_repetido_continua_sendo_erro()
    {
        // a regra antiga não afrouxou: repetir o item sem dizer quanto vai com cada um
        // continua sendo o furo que a RFQ-ERR-023 pega
        var w = await BuildAsync();
        var item = w.Q.Items.Single().Id;

        var (_, erro) = await w.Rfq.AwardByItemAsync(Carla, w.Q.Id,
        [
            new AwardInput("", PropostaDe(w.Q, w.Alfa.Id), null, "Uma", item),
            new AwardInput("", PropostaDe(w.Q, w.Beta.Id), null, "Outra", item),
        ]);

        Assert.Equal("RFQ-ERR-023", erro!.Code);
        Assert.Contains("mais de uma vez", erro.Message);
    }

    [Fact]
    public async Task Cada_fornecedor_recebe_a_O_C_com_a_quantidade_que_ganhou()
    {
        // é o teste que fecha a história: a O.C. com a quantidade cotada mandaria ao
        // fornecedor o dobro do que ele venceu
        var w = await BuildAsync();
        await w.Rfq.AwardByItemAsync(Carla, w.Q.Id, Dividir(w, 700, 300));
        var aprovador = new Actor(Guid.NewGuid(), "Marcos", Roles.SystemAdministrator);
        await w.Rfq.ManagerDecisionAsync(aprovador, w.Q.Id, "APROVAR", null);
        await w.Rfq.DirectorDecisionAsync(
            new Actor(Guid.NewGuid(), "Paula", Roles.SystemAdministrator), w.Q.Id, "APROVAR", null);

        var (comAlfa, erroAlfa) = await w.Rfq.RegisterErpPurchaseOrderAsync(
            Carla, w.Q.Id, "4521", null, null, supplierId: w.Alfa.Id);
        Assert.Null(erroAlfa);
        var (comBeta, erroBeta) = await w.Rfq.RegisterErpPurchaseOrderAsync(
            Carla, w.Q.Id, "4522", null, null, supplierId: w.Beta.Id);
        Assert.Null(erroBeta);

        var pedidoAlfa = await w.Db.PurchaseOrders.Include(o => o.Items)
            .SingleAsync(o => o.SupplierId == w.Alfa.Id);
        var pedidoBeta = await w.Db.PurchaseOrders.Include(o => o.Items)
            .SingleAsync(o => o.SupplierId == w.Beta.Id);

        Assert.Equal(700, pedidoAlfa.Items.Single().Quantity);
        Assert.Equal(300, pedidoBeta.Items.Single().Quantity);
        Assert.Equal(350m, pedidoAlfa.FreightValue);   // 70% dos 500 que ele cotou
        Assert.Equal(90m, pedidoBeta.FreightValue);    // 30% dos 300 dele
        Assert.NotNull(comAlfa);
        Assert.NotNull(comBeta);
    }

    [Fact]
    public async Task Dividir_em_tres_tambem_fecha()
    {
        // nada na regra fala em dois: o que ela cobra é a soma
        var w = await BuildAsync();
        var item = w.Q.Items.Single().Id;
        var gama = await new SupplierService(w.Db, new RelogioFixo(DateTimeOffset.UtcNow))
            .CreateAsync(Carla.Id, "Gama LTDA", "Gama", "11222333000181", null, "81 3333-3000");
        await new SupplierService(w.Db, new RelogioFixo(DateTimeOffset.UtcNow))
            .SetHomologationAsync(gama.supplier!.Id, SupplierHomologation.Homologado);
        await w.Rfq.InviteSuppliersAsync(Carla, w.Q.Id, [gama.supplier.Id]);
        await w.Rfq.SubmitProposalAsync(w.Q.Id, gama.supplier.Id,
            new(10, "30 dias", 100, null, null, [new ProposalItemInput(item, 11m, null)]), "PORTAL", "Gama");
        var atual = (await w.Rfq.GetAsync(w.Q.Id))!;

        var (fechado, erro) = await w.Rfq.AwardByItemAsync(Carla, w.Q.Id,
        [
            new AwardInput("", PropostaDe(atual, w.Alfa.Id), null, "Maior fatia", item, 500),
            new AwardInput("", PropostaDe(atual, w.Beta.Id), null, "Segunda fatia", item, 300),
            new AwardInput("", PropostaDe(atual, gama.supplier.Id), null, "Saldo", item, 200),
        ]);

        Assert.Null(erro);
        Assert.Equal(3, fechado!.AwardList.Count);
        Assert.Equal(1000m, fechado.AwardList.Sum(a => a.Quantity ?? 0));
    }
}
