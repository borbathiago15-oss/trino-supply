using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// O cockpit da TV. O que estes testes protegem é o que faria a parede mentir para a sala:
/// um número que não bate com a Torre do comprador, um risco somado duas vezes, e o item
/// urgente que entra e não sobe para o topo do radar.
/// </summary>
public class CockpitDaTorreTests
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
    private static readonly DateOnly Hoje = new(2026, 9, 10);
    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana Solicitante", Roles.Requester);
    private static readonly Actor Bruno = new(Guid.NewGuid(), "Bruno Aprovador", Roles.Approver);
    private static readonly Actor Carla = new(Guid.NewGuid(), "Carla Compradora", Roles.PurchasingOfficer);

    private sealed record World(AppDbContext Db, TorreDeControleService Torre, RequisitionService Prs);

    private static World Build()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var clock = new FixedTimeProvider(Agora);
        return new World(db, new TorreDeControleService(db, clock),
            new RequisitionService(db, new FakeNumbers(), new CatalogService(db, clock), clock));
    }

    /// <summary>
    /// SC enviada e aprovada. Data no passado se aplica <b>depois</b> do envio: a submissão
    /// recusa data vencida (PR-ERR-050), e no mundo real o item vence porque o tempo passou,
    /// não porque alguém pediu para ontem. Cada passo é conferido — submissão que falha calada
    /// deixa a SC em rascunho, e aí a Torre não vê nada e o teste acusa o lugar errado.
    /// </summary>
    private static async Task<PurchaseRequisition> ScAsync(
        World w, DateOnly? precisaEm = null, params string[] itens)
    {
        var futura = precisaEm is { } d && d < Hoje ? Hoje.AddDays(1) : precisaEm;
        var (pr, erro) = await w.Prs.CreateAsync(Ana, "Reposição", "CC-01", "NORMAL", futura,
            (itens.Length == 0 ? ["Item"] : itens).Select(d => new ItemInput(d, 10, "UN", 100, null)).ToList());
        Assert.Null(erro);
        var (_, erroEnvio) = await w.Prs.SubmitAsync(Ana, pr!.Id);
        Assert.Null(erroEnvio);
        var (_, erroAprovacao) = await w.Prs.ApproveAsync(Bruno, pr.Id, null);
        Assert.Null(erroAprovacao);

        if (precisaEm is { } vencida && vencida < Hoje)
        {
            var tracked = await w.Db.Requisitions.SingleAsync(r => r.Id == pr.Id);
            tracked.NeededBy = vencida;
            await w.Db.SaveChangesAsync();
        }
        return pr;
    }

    // ---- os números do topo --------------------------------------------------

    [Fact]
    public async Task O_backlog_conta_item_e_nao_solicitacao()
    {
        // é a mesma unidade da Torre: uma SC de três itens é três itens na parede
        var w = Build();
        await ScAsync(w, null, "Martelete", "Pé de cabra", "Luva");

        var c = await w.Torre.CockpitAsync();

        Assert.Equal(3, c.Kpis.BacklogTotalItens);
        Assert.Equal(3, c.Pipeline.Sum(n => n.Quantidade));
    }

    [Fact]
    public async Task Item_atrasado_conta_no_atraso_e_no_risco_uma_vez_so()
    {
        // somar "atrasados + prazo estourado" daria mais itens em risco que o backlog
        // inteiro quando o mesmo item é as duas coisas
        var w = Build();
        await ScAsync(w, Hoje.AddDays(-5), "Martelete");

        var c = await w.Torre.CockpitAsync();

        Assert.Equal(1, c.Kpis.ItensAtrasados);
        Assert.Equal(1, c.Kpis.BacklogTotalItens);
        Assert.Equal(100m, c.Kpis.TaxaRiscoPct);   // e não 200%
    }

    [Fact]
    public async Task Sem_nada_aberto_os_numeros_sao_zero_e_nao_explodem()
    {
        var c = Build().Torre.CockpitAsync();

        var r = await c;
        Assert.Equal(0, r.Kpis.BacklogTotalItens);
        Assert.Equal(0, r.Kpis.TaxaRiscoPct);
        Assert.Empty(r.ExcecoesCriticas);
        // sem entrega medida no período, OTIF é nulo: 0% diria "todo mundo atrasou"
        Assert.Null(r.Kpis.OtifGeralPct);
        Assert.Equal(0, r.Kpis.OtifMedidos);
    }

    // ---- a esteira -----------------------------------------------------------

    [Fact]
    public async Task A_esteira_tem_as_etapas_da_Torre_e_nao_inclui_encerrado()
    {
        var w = Build();
        await ScAsync(w, null, "Martelete");

        var c = await w.Torre.CockpitAsync();

        Assert.DoesNotContain(c.Pipeline, n => n.Etapa == "ENCERRADO");
        // as mesmas chaves que a Torre usa: a parede e a tela falam a mesma língua
        Assert.Equal(
            TorreDeControleService.Etapas.Where(e => e.Key != "ENCERRADO").Select(e => e.Key),
            c.Pipeline.Select(n => n.Etapa));
    }

    [Fact]
    public async Task O_gargalo_sai_da_espera_do_item_mais_antigo_da_etapa()
    {
        var w = Build();
        var sc = await ScAsync(w, null, "Martelete");
        // a SC entrou na fila há quatro dias: passa de 72h, então é gargalo crítico
        var tracked = await w.Db.Requisitions.SingleAsync(r => r.Id == sc.Id);
        tracked.SubmittedAt = Agora.AddDays(-4);
        tracked.DecidedAt = Agora.AddDays(-4);
        await w.Db.SaveChangesAsync();

        var no = (await w.Torre.CockpitAsync()).Pipeline.Single(n => n.Etapa == "SOLICITACAO");

        Assert.Equal(TrinoSupply.Foundation.Api.Procurement.NoDaEsteira.Critico, no.Gargalo);
        Assert.True(no.HorasNaFila >= 72);
    }

    [Fact]
    public async Task Etapa_vazia_nao_e_gargalo()
    {
        // sem item nenhum, "há quanto tempo espera" não tem resposta — e zero não é atraso
        var c = await Build().Torre.CockpitAsync();
        Assert.All(c.Pipeline, n => Assert.Equal(
            TrinoSupply.Foundation.Api.Procurement.NoDaEsteira.Normal, n.Gargalo));
    }

    // ---- o radar de exceções -------------------------------------------------

    [Fact]
    public async Task O_atraso_critico_entra_no_radar_com_os_dias_e_o_responsavel()
    {
        var w = Build();
        await ScAsync(w, Hoje.AddDays(-3), "Martelete");

        var alerta = Assert.Single((await w.Torre.CockpitAsync()).ExcecoesCriticas);

        Assert.Equal(ExcecaoDoCockpit.AtrasoCritico, alerta.TipoAlerta);
        Assert.Equal("PR-2026-000001", alerta.CodigoReferencia);
        Assert.Contains("3d de atraso", alerta.TempoRestanteOuAtraso);
        Assert.Equal("CC-01", alerta.UnidadeCentroCusto);
    }

    [Fact]
    public async Task O_mais_grave_assume_o_topo_sozinho()
    {
        // critério de aceite 3: item urgente que entra sobe sem ninguém reordenar.
        // A SC antiga e no prazo entra primeiro no banco; a atrasada, depois.
        var w = Build();
        await ScAsync(w, Hoje.AddDays(30), "No prazo");
        await ScAsync(w, Hoje.AddDays(-1), "Atrasado");

        var radar = (await w.Torre.CockpitAsync()).ExcecoesCriticas;

        Assert.Equal(ExcecaoDoCockpit.AtrasoCritico, radar[0].TipoAlerta);
        Assert.Equal(0, radar[0].Ordem);
    }

    [Fact]
    public async Task Item_no_prazo_nao_vira_alerta()
    {
        var w = Build();
        await ScAsync(w, Hoje.AddDays(10), "Martelete");
        Assert.Empty((await w.Torre.CockpitAsync()).ExcecoesCriticas);
    }

    // ---- burndown ------------------------------------------------------------

    [Fact]
    public async Task O_burndown_separa_por_comprador_e_conta_a_pendencia_critica()
    {
        var w = Build();
        await ScAsync(w, Hoje.AddDays(-2), "Atrasado");

        var linha = Assert.Single((await w.Torre.CockpitAsync()).BurndownCompradores);

        // sem triagem ainda, a fila é de "Sem responsável" — dizer isso é melhor do
        // que somar tudo num comprador que não existe
        Assert.Equal("Sem responsável", linha.CompradorNome);
        Assert.Equal(1, linha.TotalHoje);
        Assert.Equal(1, linha.PendenciasCriticas);
    }

    [Fact]
    public async Task O_cockpit_e_a_Torre_contam_o_mesmo_atraso()
    {
        // a TV fica na sala onde o comprador trabalha: se os dois números divergirem,
        // as duas telas perdem a autoridade no mesmo instante
        var w = Build();
        await ScAsync(w, Hoje.AddDays(-1), "Martelete", "Luva");
        await ScAsync(w, Hoje.AddDays(10), "No prazo");

        var cockpit = await w.Torre.CockpitAsync();
        var torre = await w.Torre.ConsultarAsync(new FiltroTorre());

        Assert.Equal(torre.Kpis.Atrasados, cockpit.Kpis.ItensAtrasados);
    }
}
