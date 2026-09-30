using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Analytics;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O cockpit — a TV da sala de Suprimentos.
///
/// O que estes testes protegem não é o layout: é a promessa de que a parede **não
/// contradiz a tela do comprador**. Etapa, atraso e prazo estourado saem da Torre, não de
/// uma segunda conta; o dinheiro e a vazão do dia são o que o cockpit acrescenta, e cada
/// um deles tem origem datada.
/// </summary>
public class CockpitServiceTests
{
    private sealed class FakeNumbers : IPrNumberGenerator
    {
        private int _next;
        public Task<string> NextAsync(CancellationToken ct = default) =>
            Task.FromResult($"PR-2026-{++_next:000000}");
    }

    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    private static readonly DateTimeOffset Agora = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana Solicitante", Roles.Requester);
    private static readonly Actor Bruno = new(Guid.NewGuid(), "Bruno Aprovador", Roles.Approver);

    private sealed record Mundo(AppDbContext Db, CockpitService Cockpit, RequisitionService Prs);

    private static Mundo Build()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var clock = new RelogioFixo(Agora);
        var catalog = new CatalogService(db, clock);
        var torre = new TorreDeControleService(db, clock);
        return new Mundo(db, new CockpitService(db, torre, clock),
            new RequisitionService(db, new FakeNumbers(), catalog, clock));
    }

    private static async Task<PurchaseRequisition> ScAprovadaAsync(Mundo m, params string[] itens)
    {
        var (pr, _) = await m.Prs.CreateAsync(Ana, "Reposição", "CC-01", "NORMAL", null,
            itens.Select(d => new ItemInput(d, 10, "UN", 100, null)).ToList());
        await m.Prs.SubmitAsync(Ana, pr!.Id);
        await m.Prs.ApproveAsync(Bruno, pr.Id, null);
        return pr;
    }

    private static PurchaseOrder Pedido(decimal valor, DateTimeOffset criado,
        DateTimeOffset? entregue = null, DateOnly? prometido = null, int itens = 1)
    {
        var o = new PurchaseOrder
        {
            Number = $"PO-{Guid.NewGuid():N}"[..12], SupplierId = Guid.NewGuid(),
            SupplierName = "Alfa", TotalValue = valor, CreatedAt = criado, UpdatedAt = criado,
            DeliveryCompletedAt = entregue, PromisedDate = prometido,
        };
        for (var i = 0; i < itens; i++)
            o.Items.Add(new PurchaseOrderItem { Description = $"Item {i}", Quantity = 1, UnitPrice = valor });
        return o;
    }

    [Fact]
    public async Task O_valor_comprado_compara_o_mes_com_o_anterior()
    {
        // numa TV, um número sozinho não diz para que lado se anda
        var m = Build();
        m.Db.PurchaseOrders.Add(Pedido(10_000, new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.Zero)));
        m.Db.PurchaseOrders.Add(Pedido(4_000, new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero)));
        m.Db.PurchaseOrders.Add(Pedido(9_000, new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero)));
        await m.Db.SaveChangesAsync();

        var c = await m.Cockpit.LerAsync();

        Assert.Equal(14_000, c.Executivo.ValorComprado.Valor);
        Assert.Equal(9_000, c.Executivo.ValorComprado.Anterior);
    }

    [Fact]
    public async Task Pedido_cancelado_nao_entra_no_valor_comprado()
    {
        var m = Build();
        var vivo = Pedido(10_000, new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.Zero));
        var morto = Pedido(50_000, new DateTimeOffset(2026, 9, 6, 9, 0, 0, TimeSpan.Zero));
        morto.Status = PurchaseOrderStatus.Cancelled;
        m.Db.PurchaseOrders.AddRange(vivo, morto);
        await m.Db.SaveChangesAsync();

        var c = await m.Cockpit.LerAsync();

        Assert.Equal(10_000, c.Executivo.ValorComprado.Valor);
    }

    [Fact]
    public async Task O_OTIF_conta_so_o_que_ja_foi_entregue()
    {
        // pedido em trânsito não é acerto nem atraso: contá-lo como qualquer um dos dois
        // falsearia o percentual que fica na parede
        var m = Build();
        var setembro = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
        m.Db.PurchaseOrders.Add(Pedido(100, setembro, entregue: setembro, prometido: new DateOnly(2026, 9, 12)));
        m.Db.PurchaseOrders.Add(Pedido(100, setembro, entregue: setembro, prometido: new DateOnly(2026, 9, 8)));
        m.Db.PurchaseOrders.Add(Pedido(100, setembro));   // ainda em trânsito
        await m.Db.SaveChangesAsync();

        var c = await m.Cockpit.LerAsync();

        Assert.Equal(2, c.Executivo.EntregasConcluidas);   // o terceiro não conta
        Assert.Equal(1, c.Executivo.EntregasNoPrazo);
        Assert.Equal(50m, c.Executivo.EntregasNoPrazoPercent);
    }

    [Fact]
    public async Task Sem_entrega_concluida_o_OTIF_e_nulo_em_vez_de_zero()
    {
        // 0% diria "erramos todas"; nulo diz "ainda não há o que medir", que é a verdade
        var m = Build();
        m.Db.PurchaseOrders.Add(Pedido(100, new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero)));
        await m.Db.SaveChangesAsync();

        var c = await m.Cockpit.LerAsync();

        Assert.Null(c.Executivo.EntregasNoPrazoPercent);
        Assert.Equal(0, c.Executivo.EntregasConcluidas);
    }

    [Fact]
    public async Task Demanda_e_capacidade_saem_dos_dois_extremos_do_mesmo_cano()
    {
        // é a pergunta que o documento chama de mais importante: o time acompanha a
        // entrada de demanda? Entrou por item, concluiu por item
        var m = Build();
        await ScAprovadaAsync(m, "Martelete", "Pé de cabra", "Luva");
        m.Db.PurchaseOrders.Add(Pedido(500, Agora.AddHours(-3), entregue: Agora.AddHours(-1), itens: 2));
        await m.Db.SaveChangesAsync();

        var c = await m.Cockpit.LerAsync();

        Assert.Equal(3, c.Produtividade.EntraramHoje);
        Assert.Equal(2, c.Produtividade.ConcluidosHoje);
        Assert.Equal(1, c.Produtividade.Saldo);              // positivo: backlog crescendo
        Assert.Equal(66.7m, c.Produtividade.TaxaDeConclusao);
    }

    [Fact]
    public async Task Sem_nada_entrando_a_taxa_de_conclusao_e_nula_em_vez_de_zero()
    {
        // dividir por zero não dá 0%: dá pergunta sem sentido, e a TV não deve fingir
        var m = Build();

        var c = await m.Cockpit.LerAsync();

        Assert.Equal(0, c.Produtividade.EntraramHoje);
        Assert.Null(c.Produtividade.TaxaDeConclusao);
    }

    [Fact]
    public async Task Os_numeros_de_etapa_sao_os_mesmos_da_Torre()
    {
        // a promessa que sustenta a TV: se a parede e a tela do comprador discordarem,
        // ele para de acreditar nas duas
        var m = Build();
        await ScAprovadaAsync(m, "Martelete", "Pé de cabra");
        var torre = new TorreDeControleService(m.Db, new RelogioFixo(Agora));
        var kpis = (await torre.ConsultarAsync(new FiltroTorre())).Kpis;

        var c = await m.Cockpit.LerAsync();

        Assert.Equal(kpis.PrecisaDeVoce, c.Produtividade.Backlog);
        Assert.Equal(kpis.EmCotacao, c.Produtividade.EmCotacao);
        Assert.Equal(kpis.AguardandoAprovacao, c.Produtividade.AguardandoAprovacao);
        Assert.Equal(kpis.Total, c.Executivo.EmAndamento);
        var solicitacao = c.Produtividade.Fluxo.Single(e => e.Key == "SOLICITACAO");
        Assert.Equal(kpis.Novos, solicitacao.Quantidade);
    }

    [Fact]
    public async Task O_gargalo_aponta_a_etapa_mais_cheia_e_e_nulo_quando_nao_ha_nada()
    {
        // "onde precisamos atuar" vale mais que seis números iguais lado a lado
        var vazio = await Build().Cockpit.LerAsync();
        Assert.Null(vazio.Produtividade.Gargalo);

        var m = Build();
        await ScAprovadaAsync(m, "Martelete", "Pé de cabra", "Luva");

        var c = await m.Cockpit.LerAsync();

        Assert.NotNull(c.Produtividade.Gargalo);
        Assert.Equal("SOLICITACAO", c.Produtividade.Gargalo!.Key);
        Assert.Equal(3, c.Produtividade.Gargalo.Quantidade);
    }

    [Fact]
    public async Task O_fluxo_nao_mostra_a_etapa_encerrada()
    {
        // a TV é sobre o que está em andamento; "encerrado" não pede ação de ninguém
        var m = Build();

        var c = await m.Cockpit.LerAsync();

        Assert.DoesNotContain(c.Produtividade.Fluxo, e => e.Key == "ENCERRADO");
        Assert.Equal(5, c.Produtividade.Fluxo.Count);
    }

    [Fact]
    public async Task Alerta_zerado_nao_vira_linha_na_faixa()
    {
        // alarme que grita sempre para de ser lido
        var m = Build();

        var c = await m.Cockpit.LerAsync();

        Assert.Empty(c.Alertas);
        Assert.All(c.Alertas, a => Assert.True(a.Count > 0));
    }

    [Fact]
    public async Task A_leitura_carrega_a_hora_dela()
    {
        // a tela gira e recarrega sozinha; sem a hora do servidor, ninguém sabe se o que
        // está na parede é de agora ou de uma requisição que travou
        var c = await Build().Cockpit.LerAsync();

        Assert.Equal(Agora, c.At);
    }
}
