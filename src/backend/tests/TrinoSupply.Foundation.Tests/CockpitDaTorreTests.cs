using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Materials;
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

    private sealed record World(AppDbContext Db, TorreDeControleService Torre, RequisitionService Prs,
        MaterialRequisitionService Mrs, CatalogService Catalog);

    private static World Build()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var clock = new FixedTimeProvider(Agora);
        var catalogo = new CatalogService(db, clock);
        return new World(db, new TorreDeControleService(db, clock),
            new RequisitionService(db, new FakeNumbers(), catalogo, clock),
            new MaterialRequisitionService(db, catalogo, clock), catalogo);
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

    // ---- a vazão do dia --------------------------------------------------------
    //
    // O backlog diz quanto há parado; ele não diz se o time está ganhando ou perdendo
    // terreno. Entrou × concluiu responde isso nos dois extremos do mesmo cano.

    [Fact]
    public async Task A_vazao_conta_por_item_e_o_saldo_diz_para_que_lado_o_dia_andou()
    {
        // a mesma unidade do resto da Torre: uma SC de três itens é três demandas
        var w = Build();
        await ScAsync(w, null, "Martelete", "Pé de cabra", "Luva");

        var c = await w.Torre.CockpitAsync();

        Assert.Equal(3, c.Vazao.EntraramHoje);
        Assert.Equal(0, c.Vazao.ConcluidosHoje);
        Assert.Equal(3, c.Vazao.Saldo);              // positivo: backlog crescendo
        Assert.Equal(0m, c.Vazao.TaxaConclusaoPct);
    }

    [Fact]
    public async Task O_que_foi_entregue_hoje_conta_na_capacidade_mesmo_estando_encerrado()
    {
        // é o caso que zeraria o lado da capacidade se a conta ficasse dentro do "aberto":
        // o item concluído HOJE é, por definição, encerrado
        var w = Build();
        var sc = await ScAsync(w, null, "Martelete", "Pé de cabra");
        w.Db.PurchaseOrders.Add(new PurchaseOrder
        {
            Number = "PO-1", SupplierId = Guid.NewGuid(), SupplierName = "Alfa",
            SourcePrId = sc.Id, TotalValue = 1000, CreatedAt = Agora.AddHours(-4),
            UpdatedAt = Agora, DeliveryCompletedAt = Agora.AddHours(-1),
            Status = PurchaseOrderStatus.Received,
        });
        await w.Db.SaveChangesAsync();

        var c = await w.Torre.CockpitAsync();

        Assert.Equal(2, c.Vazao.EntraramHoje);
        Assert.Equal(2, c.Vazao.ConcluidosHoje);
        Assert.Equal(0, c.Vazao.Saldo);              // empate: backlog estável
        Assert.Equal(100m, c.Vazao.TaxaConclusaoPct);
    }

    [Fact]
    public async Task Entrega_de_ontem_nao_conta_como_capacidade_de_hoje()
    {
        var w = Build();
        var sc = await ScAsync(w, null, "Martelete");
        w.Db.PurchaseOrders.Add(new PurchaseOrder
        {
            Number = "PO-1", SupplierId = Guid.NewGuid(), SupplierName = "Alfa",
            SourcePrId = sc.Id, TotalValue = 1000, CreatedAt = Agora.AddDays(-3),
            UpdatedAt = Agora, DeliveryCompletedAt = Agora.AddDays(-1),
            Status = PurchaseOrderStatus.Received,
        });
        await w.Db.SaveChangesAsync();

        var c = await w.Torre.CockpitAsync();

        Assert.Equal(0, c.Vazao.ConcluidosHoje);
    }

    [Fact]
    public async Task Dia_sem_entrada_tem_taxa_nula_e_nao_zero()
    {
        // dividir por zero não dá 0%: dá pergunta sem sentido. "Nada entrou" e "não demos
        // conta de nada" são notícias diferentes, e só uma delas cobra alguém
        var c = await Build().Torre.CockpitAsync();

        Assert.Equal(0, c.Vazao.EntraramHoje);
        Assert.Null(c.Vazao.TaxaConclusaoPct);
        Assert.Equal(0, c.Vazao.Saldo);
    }

    // ---- o bloco do almoxarifado -----------------------------------------------
    //
    // A solicitação de material é o outro cano da casa, e a parede fica na sala onde o
    // atendimento acontece. O que estes testes protegem: as duas filas não se somam (a vez é
    // de gente diferente), o atendimento parcial conta pela quantidade, e mês sem atendimento
    // não vira "0% atendido pelo estoque".

    /// <summary>
    /// Solicitação de material com um produto de catálogo por quantidade informada. Passa pelo
    /// serviço de verdade de propósito: o cockpit deriva do que o atendimento grava, e um
    /// registro montado à mão poderia não ter as marcas que a conta lê.
    /// </summary>
    private static async Task<MaterialRequisition> MrAsync(
        World w, string centro, bool aprovar, params decimal[] quantidades)
    {
        var itens = new List<MaterialItemInput>();
        foreach (var q in quantidades)
        {
            var (produto, erroProduto) = await w.Catalog.CreateAsync(Carla.Id,
                $"MAT-{Guid.NewGuid():N}"[..12], "Detergente neutro", "MATERIAL DE LIMPEZA", "UN", 5m);
            Assert.Null(erroProduto);
            itens.Add(new MaterialItemInput(produto!.Id, q));
        }
        var (mr, erro) = await w.Mrs.CreateAsync(Ana, centro, null, itens);
        Assert.Null(erro);
        if (!aprovar) return mr!;
        var (aprovada, erroAprovacao) = await w.Mrs.ApproveAsync(Bruno, mr!.Id, null, null);
        Assert.Null(erroAprovacao);
        return aprovada!;
    }

    /// <summary>Recua a liberação do Nível 1, que é o relógio da fila do almoxarifado.</summary>
    private static async Task LiberadaHaAsync(World w, Guid id, int horas)
    {
        var mr = await w.Db.MaterialRequisitions.SingleAsync(r => r.Id == id);
        mr.ApprovedAt = Agora.AddHours(-horas);
        await w.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_fila_do_almoxarifado_nao_soma_o_que_ainda_espera_o_centro_de_custo()
    {
        // somar as duas cobraria do almoxarife trabalho que o Nível 1 ainda não liberou — é o
        // mesmo erro que separou "em faturamento" de "aguardando recebimento" na Torre
        var w = Build();
        await MrAsync(w, "CC-01", true, 10, 4);      // liberada: fila do estoque
        await MrAsync(w, "CC-01", false, 3);         // ainda com o Nível 1

        var a = (await w.Torre.CockpitAsync()).Almoxarifado;

        Assert.Equal(1, a.FilaSolicitacoes);
        Assert.Equal(2, a.FilaItens);                // o que se separa da prateleira
        Assert.Equal(1, a.AguardandoAprovacao);
    }

    [Fact]
    public async Task A_fila_conta_da_liberacao_do_nivel_1_e_acende_como_a_esteira()
    {
        // usar a criação contaria como espera do almoxarifado o tempo em que a solicitação
        // ainda estava na mão do gestor do centro
        var w = Build();
        var antiga = await MrAsync(w, "CC-01", true, 10);
        await LiberadaHaAsync(w, antiga.Id, 80);
        await MrAsync(w, "CC-01", true, 5);          // liberada agora

        var a = (await w.Torre.CockpitAsync()).Almoxarifado;

        Assert.Equal(80, a.HorasDoMaisAntigo);       // o mais antigo, não a média
        Assert.Equal(NoDaEsteira.Critico, a.Gargalo);
        Assert.Equal(antiga.Number, a.MaisAntigaNumero);
    }

    [Fact]
    public async Task O_atendimento_parcial_conta_pela_quantidade_e_o_que_faltou_vira_compra()
    {
        // entregar 8 de 10 é 80% atendido: contar a solicitação inteira como não atendida
        // esconderia as oito unidades que saíram do estoque
        var w = Build();
        var mr = await MrAsync(w, "CC-01", true, 10);
        await LiberadaHaAsync(w, mr.Id, 6);
        var item = (await w.Db.MaterialRequisitions.Include(r => r.Items)
            .SingleAsync(r => r.Id == mr.Id)).Items.Single();
        var (_, erro) = await w.Mrs.FulfillAsync(Carla, mr.Id, [new(item.Id, 8)], w.Prs);
        Assert.Null(erro);

        var a = (await w.Torre.CockpitAsync()).Almoxarifado;

        Assert.Equal(1, a.AtendidasHoje);
        Assert.Equal(80m, a.AtendidoPeloEstoquePct);
        Assert.Equal(1, a.ViraramCompraNoMes);
        Assert.Equal(6m, a.HorasMediaAtendimento);
        Assert.Equal(0, a.FilaSolicitacoes);         // saiu da fila do estoque
    }

    [Fact]
    public async Task Mes_sem_atendimento_deixa_o_percentual_e_o_tempo_nulos()
    {
        // 0% diria que o estoque estava vazio; nulo diz que ninguém atendeu nada ainda
        var w = Build();
        await MrAsync(w, "CC-01", true, 10);

        var a = (await w.Torre.CockpitAsync()).Almoxarifado;

        Assert.Null(a.AtendidoPeloEstoquePct);
        Assert.Null(a.HorasMediaAtendimento);
        Assert.Equal(0, a.AtendidasHoje);
    }

    [Fact]
    public async Task A_unidade_da_parede_sai_do_centro_de_custo_porque_a_solicitacao_nao_tem_empresa()
    {
        // a MR não grava empresa: o caminho até a unidade é CostCenter.CompanyId → LegalName,
        // que é o mesmo texto que a SC grava. Centro sem empresa conta só na visão geral —
        // pôr a solicitação numa unidade escolhida ao acaso seria inventar o dado que falta
        var w = Build();
        var empresa = new Company
        {
            LegalName = "TRINO ALIMENTOS LTDA", TaxId = "11222333000144",
            Address = "Rua 1", City = "Recife", State = "PE", Zip = "50000000",
            CreatedAt = Agora, UpdatedAt = Agora,
        };
        w.Db.Companies.Add(empresa);
        w.Db.CostCenters.Add(new CostCenter
        {
            Code = "CC-ALI", Name = "Alimentos", CompanyId = empresa.Id,
            CreatedAt = Agora, UpdatedAt = Agora,
        });
        await w.Db.SaveChangesAsync();
        await MrAsync(w, "CC-ALI", true, 10);
        await MrAsync(w, "CC-SEM", true, 4);         // centro sem empresa cadastrada

        var geral = (await w.Torre.CockpitAsync()).Almoxarifado;
        var recorte = (await w.Torre.CockpitAsync("TRINO ALIMENTOS LTDA")).Almoxarifado;

        Assert.Equal(2, geral.FilaSolicitacoes);
        Assert.Equal(1, recorte.FilaSolicitacoes);
    }
}
