using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// Torre de Controle. O que estes testes protegem é o que faria a tela mentir:
/// juntar itens que estão em etapas diferentes numa linha só, e devolver uma
/// página cujo total não fecha com o filtro que a pessoa aplicou.
/// </summary>
public class TorreDeControleServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeNumbers : IPrNumberGenerator
    {
        private int _next;
        public Task<string> NextAsync(CancellationToken ct = default) =>
            Task.FromResult($"PR-2026-{++_next:000000}");
    }

    private static readonly DateTimeOffset Agora = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana Solicitante", Roles.Requester);
    private static readonly Actor Bruno = new(Guid.NewGuid(), "Bruno Aprovador", Roles.Approver);
    private static readonly Actor Carla = new(Guid.NewGuid(), "Carla Compradora", Roles.PurchasingOfficer);

    private sealed record World(AppDbContext Db, TorreDeControleService Torre,
        RequisitionService Prs, QuotationService Rfq, SupplierService Sup, CatalogService Catalog);

    private static World Build()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var clock = new FixedTimeProvider(Agora);
        var catalog = new CatalogService(db, clock);
        return new World(db, new TorreDeControleService(db, clock),
            new RequisitionService(db, new FakeNumbers(), catalog, clock),
            new QuotationService(db, clock), new SupplierService(db, clock), catalog);
    }

    private static async Task<PurchaseRequisition> ScAprovadaAsync(World w, params string[] descricoes)
    {
        var (pr, _) = await w.Prs.CreateAsync(Ana, "Reposição", "CC-01", "NORMAL", null,
            descricoes.Select(d => new ItemInput(d, 10, "UN", 100, null)).ToList());
        await w.Prs.SubmitAsync(Ana, pr!.Id);
        await w.Prs.ApproveAsync(Bruno, pr.Id, null);
        return pr;
    }

    [Fact]
    public async Task Uma_linha_por_item_e_nao_uma_por_solicitacao()
    {
        // é a razão de a Torre existir: numa SC de três itens, olhar a SC inteira
        // esconde qual deles está parado
        var w = Build();
        await ScAprovadaAsync(w, "Martelete", "Pé de cabra", "Luva");

        var pagina = await w.Torre.ConsultarAsync(new FiltroTorre());

        Assert.Equal(3, pagina.Total);
        Assert.Equal(3, pagina.Items.Count);
        Assert.Equal(["Martelete", "Pé de cabra", "Luva"],
            pagina.Items.OrderBy(i => i.Sequence).Select(i => i.Description));
        Assert.All(pagina.Items, i => Assert.Equal("PR-2026-000001", i.PrNumber));
        // sem cotação ainda: os três estão na etapa da solicitação
        Assert.All(pagina.Items, i => Assert.Equal("SOLICITACAO", i.Stage));
    }

    [Fact]
    public async Task Itens_da_mesma_SC_podem_estar_em_etapas_diferentes()
    {
        // é o critério de aceite do documento: "itens de uma mesma solicitação podem
        // estar em etapas diferentes simultaneamente"
        var w = Build();
        var comCotacao = await ScAprovadaAsync(w, "Martelete");
        await ScAprovadaAsync(w, "Luva");
        await w.Rfq.CreateFromPrAsync(Carla, comCotacao.Id, QuotationKind.Purchase, null, null);

        var pagina = await w.Torre.ConsultarAsync(new FiltroTorre());

        var martelete = pagina.Items.Single(i => i.Description == "Martelete");
        var luva = pagina.Items.Single(i => i.Description == "Luva");
        Assert.Equal("COTACAO", martelete.Stage);
        Assert.Equal("Cotação", martelete.StageLabel);
        Assert.Equal("SOLICITACAO", luva.Stage);
    }

    [Fact]
    public async Task A_pagina_fecha_com_o_total_mesmo_no_filtro_derivado()
    {
        // etapa não é coluna: filtrar por ela depois do corte devolveria três linhas
        // numa página de cinquenta, com o total dizendo outra coisa
        var w = Build();
        var comCotacao = await ScAprovadaAsync(w, "Martelete", "Pé de cabra");
        await ScAprovadaAsync(w, "Luva", "Bota", "Capacete");
        await w.Rfq.CreateFromPrAsync(Carla, comCotacao.Id, QuotationKind.Purchase, null, null);

        var tudo = await w.Torre.ConsultarAsync(new FiltroTorre());
        Assert.Equal(5, tudo.Total);

        var cotando = await w.Torre.ConsultarAsync(new FiltroTorre(Stage: "COTACAO"));
        Assert.Equal(2, cotando.Total);
        Assert.Equal(2, cotando.Items.Count);
        Assert.All(cotando.Items, i => Assert.Equal("COTACAO", i.Stage));

        var solicitando = await w.Torre.ConsultarAsync(new FiltroTorre(Stage: "SOLICITACAO"));
        Assert.Equal(3, solicitando.Total);
    }

    [Fact]
    public async Task A_pagina_corta_e_o_total_continua_o_da_base()
    {
        var w = Build();
        await ScAprovadaAsync(w, "Item um", "Item dois", "Item três", "Item quatro", "Item cinco");

        var primeira = await w.Torre.ConsultarAsync(new FiltroTorre(Page: 1, PageSize: 2));
        Assert.Equal(5, primeira.Total);
        Assert.Equal(3, primeira.Pages);
        Assert.Equal(2, primeira.Items.Count);

        var terceira = await w.Torre.ConsultarAsync(new FiltroTorre(Page: 3, PageSize: 2));
        Assert.Single(terceira.Items);
        Assert.Equal(5, terceira.Total);
        // nenhum item se repete entre as páginas
        Assert.Empty(primeira.Items.Select(i => i.ItemId).Intersect(terceira.Items.Select(i => i.ItemId)));
    }

    [Fact]
    public async Task Rascunho_fica_de_fora_da_fila_do_comprador()
    {
        // rascunho é da pessoa que escreve, não chegou para o comprador
        var w = Build();
        await w.Prs.CreateAsync(Ana, "Ainda pensando", "CC-01", "NORMAL", null,
            [new ItemInput("Item de rascunho", 1, "UN", 10, null)]);
        await ScAprovadaAsync(w, "Item enviado");

        var pagina = await w.Torre.ConsultarAsync(new FiltroTorre());

        Assert.Equal("Item enviado", Assert.Single(pagina.Items).Description);
    }

    [Fact]
    public async Task Atraso_olha_a_data_prometida_e_nao_persegue_o_que_ja_encerrou()
    {
        // a SC nasce com data no futuro — o sistema recusa data de necessidade no
        // passado (PR-ERR-050). O que faz um item atrasar é o tempo passar, e é
        // assim que este teste o produz: consulta a Torre com o relógio adiante.
        var w = Build();
        var (venceu, erroCriar) = await w.Prs.CreateAsync(Ana, "Urgente", "CC-01", "URGENT",
            new DateOnly(2026, 9, 15),
            [new ItemInput("Correia", 5, "UN", 50, null)], null,
            new RequisitionService.ScHeaderInput(null, null, null, null, "linha parada", "produção para"));
        Assert.Null(erroCriar);
        await w.Prs.SubmitAsync(Ana, venceu!.Id);
        await w.Prs.ApproveAsync(Bruno, venceu.Id, null);

        var (folgada, _) = await w.Prs.CreateAsync(Ana, "Tranquilo", "CC-01", "NORMAL",
            new DateOnly(2026, 12, 1), [new ItemInput("Rolamento", 5, "UN", 50, null)]);
        await w.Prs.SubmitAsync(Ana, folgada!.Id);
        await w.Prs.ApproveAsync(Bruno, folgada.Id, null);

        // no dia 10 nada está atrasado
        var antes = await w.Torre.ConsultarAsync(new FiltroTorre());
        Assert.All(antes.Items, i => Assert.False(i.Late));

        // no dia 20, a correia venceu e o rolamento não
        var depois = new TorreDeControleService(w.Db,
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero)));
        var pagina = await depois.ConsultarAsync(new FiltroTorre());
        Assert.True(pagina.Items.Single(i => i.Description == "Correia").Late);
        Assert.False(pagina.Items.Single(i => i.Description == "Rolamento").Late);

        var so = await depois.ConsultarAsync(new FiltroTorre(Late: true));
        Assert.Equal("Correia", Assert.Single(so.Items).Description);
        Assert.Equal(1, so.Total);
    }

    [Fact]
    public async Task Busca_acha_pelo_numero_da_SC_e_pela_descricao_do_item()
    {
        var w = Build();
        await ScAprovadaAsync(w, "Martelete rebatedor");
        await ScAprovadaAsync(w, "Luva de vaqueta");

        Assert.Equal("Martelete rebatedor",
            Assert.Single((await w.Torre.ConsultarAsync(new FiltroTorre(Search: "martelete"))).Items).Description);
        Assert.Equal("Luva de vaqueta",
            Assert.Single((await w.Torre.ConsultarAsync(new FiltroTorre(Search: "PR-2026-000002"))).Items).Description);
    }

    [Fact]
    public async Task Os_KPIs_contam_a_base_inteira_e_nao_a_pagina()
    {
        // KPI que muda ao virar a página não é indicador, é contagem de tela
        var w = Build();
        await ScAprovadaAsync(w, "Item um", "Item dois", "Item três", "Item quatro", "Item cinco");

        var pagina = await w.Torre.ConsultarAsync(new FiltroTorre(Page: 1, PageSize: 2));

        Assert.Equal(2, pagina.Items.Count);
        Assert.Equal(5, pagina.Kpis.Total);
        Assert.Equal(5, pagina.Kpis.Novos);
    }

    [Fact]
    public void Cada_situacao_cai_numa_etapa_conhecida()
    {
        // a etapa é função da situação: nenhuma pode ficar sem casa
        var chaves = new[]
        {
            "RASCUNHO", "PENDENTE", "DEVOLVIDO", "EM_COTACAO", "AGUARDANDO_APROVACAO",
            "PEDIDO_APROVADO", "OC_FATURAMENTO", "PEDIDO_ENTREGUE", "PEDIDO_REJEITADO",
            "CANCELADO_PARCIAL",
        };
        foreach (var chave in chaves)
        {
            var etapa = TorreDeControleService.EtapaDe(chave);
            Assert.Contains(etapa, TorreDeControleService.Etapas.Select(e => e.Key));
            Assert.NotEqual(etapa, TorreDeControleService.RotuloDaEtapa(etapa));
        }
    }

    [Fact]
    public void A_torre_e_de_quem_trabalha_a_fila_de_compras()
    {
        foreach (var papel in new[] { Roles.PurchasingOfficer, Roles.SupplyManager,
                                      Roles.SystemAdministrator, Roles.Director, Roles.Auditor })
            Assert.True(TorreDeControleService.CanView(papel));
        foreach (var papel in new[] { Roles.Requester, Roles.Approver, Roles.WarehouseOperator })
            Assert.False(TorreDeControleService.CanView(papel));
    }
}
