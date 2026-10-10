using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Analytics;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Materials;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// A segunda tela da parede. O que estes testes protegem é o que faria a TV discordar do
/// painel do almoxarife na sala ao lado: um "fora do prazo" contado por outra régua, um
/// recorte de unidade que inventa a empresa da solicitação, e o estourado que não sobe ao topo.
/// </summary>
public class CockpitDoMaterialTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Hoje = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Beto = Guid.NewGuid();

    private sealed record World(
        CockpitDoMaterialService Parede, AnalyticsDeMaterialService Painel, TorreDeControleService Torre,
        AppDbContext Db, CatalogItem Luva, CatalogItem Papel);

    private static async Task<World> BuildAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var clock = new FixedTimeProvider(Hoje);
        var catalog = new CatalogService(db, clock);
        var (luva, _) = await catalog.CreateAsync(Ana, "EPI-001", "Luva nitrílica", "EPI", "PAR", 12m);
        var (papel, _) = await catalog.CreateAsync(Ana, "ESC-001", "Papel A4", "ESCRITORIO", "PC", 25m);
        // duas unidades: a solicitação entra na da empresa do centro de custo
        var pb = new Company { LegalName = "Trino PB", TaxId = "11111111000111" };
        var ba = new Company { LegalName = "Trino BA", TaxId = "22222222000122" };
        db.Companies.AddRange(pb, ba);
        db.CostCenters.AddRange(
            new CostCenter { Code = "PB-001", Name = "Obra Paraíba", CompanyId = pb.Id, Active = true, CreatedAt = Hoje, UpdatedAt = Hoje },
            new CostCenter { Code = "BA-001", Name = "Obra Bahia", CompanyId = ba.Id, Active = true, CreatedAt = Hoje, UpdatedAt = Hoje },
            new CostCenter { Code = "SEM-EMP", Name = "Sem empresa", Active = true, CreatedAt = Hoje, UpdatedAt = Hoje });
        await db.SaveChangesAsync();
        var prazos = new PrazoDeAtendimentoService(db, clock);
        var painel = new AnalyticsDeMaterialService(db, clock, prazos);
        var torre = new TorreDeControleService(db, clock);
        return new World(new CockpitDoMaterialService(db, clock, torre, painel, prazos), painel, torre, db, luva!, papel!);
    }

    private static DateTimeOffset Dia(int mes, int dia, int hora = 9) => new(2026, mes, dia, hora, 0, 0, TimeSpan.Zero);

    private static MaterialRequisition Mr(string numero, string centro, Guid quem, string rotulo, DateTimeOffset criada,
        MaterialRequisitionStatus status, params (CatalogItem item, decimal qtd, decimal entregue)[] itens)
    {
        var mr = new MaterialRequisition
        {
            Number = numero, CostCenter = centro, RequesterId = quem, RequesterLabel = rotulo,
            CreatedAt = criada, UpdatedAt = criada, Status = status,
        };
        foreach (var (item, qtd, entregue) in itens)
            mr.Items.Add(new MaterialRequisitionItem
            {
                RequisitionId = mr.Id, CatalogItemId = item.Id, CatalogCode = item.Code, Description = item.Description,
                UnitOfMeasure = item.UnitOfMeasure, Quantity = qtd, FulfilledQuantity = entregue, CreatedAt = criada,
                Status = entregue >= qtd ? MaterialItemStatus.Fulfilled : entregue > 0 ? MaterialItemStatus.PartiallyFulfilled
                    : MaterialItemStatus.Pending,
            });
        return mr;
    }

    private static async Task<World> CenarioAsync()
    {
        var w = await BuildAsync();
        // atendida no prazo (2 dias depois da liberação), na PB
        var m1 = Mr("MR-1", "PB-001", Ana, "Ana", Dia(10, 1), MaterialRequisitionStatus.Fulfilled, (w.Luva, 10, 10));
        m1.ApprovedAt = Dia(10, 1, 13); m1.FulfilledAt = Dia(10, 3, 13);
        // aguardando o centro há 5 dias, na BA: acende na fila do centro
        var m2 = Mr("MR-2", "BA-001", Beto, "Beto", Dia(10, 2), MaterialRequisitionStatus.Submitted, (w.Papel, 5, 0));
        // na fila do estoque há 4 dias com prazo padrão de 2: estourada, na PB
        var m3 = Mr("MR-3", "PB-001", Ana, "Ana", Dia(10, 3), MaterialRequisitionStatus.Approved, (w.Luva, 2, 0));
        m3.ApprovedAt = Dia(10, 3);
        // liberada hoje: dentro do prazo, não entra no radar
        var m4 = Mr("MR-4", "PB-001", Beto, "Beto", Dia(10, 7, 8), MaterialRequisitionStatus.Approved, (w.Papel, 1, 0));
        m4.ApprovedAt = Dia(10, 7, 9);
        // atendida em parte e o resto virou compra, num centro sem empresa: só na visão geral
        var m5 = Mr("MR-5", "SEM-EMP", Ana, "Ana", Dia(10, 4), MaterialRequisitionStatus.PurchaseRoute, (w.Luva, 6, 2));
        m5.ApprovedAt = Dia(10, 4, 10); m5.FulfilledAt = Dia(10, 5, 10);
        m5.PurchaseRequisitionId = Guid.NewGuid(); m5.PurchaseRequisitionNumber = "PR-2026-000099";
        w.Db.MaterialRequisitions.AddRange(m1, m2, m3, m4, m5);
        await w.Db.SaveChangesAsync();
        return w;
    }

    [Fact]
    public async Task A_parede_diz_o_mesmo_numero_que_o_painel_e_que_a_tela_da_compra()
    {
        var w = await CenarioAsync();
        var parede = await w.Parede.ObterAsync();
        var painel = await w.Painel.MaterialAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7), null, null, null);
        var compra = await w.Torre.CockpitAsync();

        Assert.Equal(painel.Kpis.SlaBreachedOpen, parede.ForaDoPrazo);
        Assert.Equal(1, parede.ForaDoPrazo);
        Assert.Equal(painel.Kpis.SlaMetPercent, parede.AtendidasNoPrazoPct);
        Assert.Equal(painel.Kpis.SlaMeasured, parede.AtendidasMedidas);
        Assert.Equal(painel.Kpis.Requested, parede.SolicitadasNoMes);
        Assert.Equal(painel.Kpis.Fulfilled, parede.AtendidasNoMes);
        Assert.Equal(painel.ByCostCenter.Take(5).Select(l => l.Label), parede.PorCentro.Select(l => l.Rotulo));
        // o bloco do almoxarifado é o mesmo objeto nas duas telas
        Assert.Equal(compra.Almoxarifado, parede.Almoxarifado);
        Assert.Equal(2, parede.Almoxarifado.FilaSolicitacoes);
        Assert.Equal(1, parede.Almoxarifado.AguardandoAprovacao);
    }

    [Fact]
    public async Task O_radar_poe_o_prazo_estourado_no_topo_e_diz_de_quem_e_a_vez()
    {
        var w = await CenarioAsync();
        var radar = (await w.Parede.ObterAsync()).Radar;

        Assert.Equal(AlertaDoAlmoxarifado.PrazoEstourado, radar[0].Tipo);
        Assert.Equal("MR-3", radar[0].Numero);
        Assert.Equal("Obra Paraíba", radar[0].CentroCusto);   // o nome do centro, não o código
        Assert.Contains("4d na fila · prazo 2d", radar[0].Tempo);
        // a fila do centro acende pela régua de gargalo da esteira (48h), e diz de quem é a vez
        var centro = Assert.Single(radar, x => x.Tipo == AlertaDoAlmoxarifado.AguardandoOCentro);
        Assert.Equal("MR-2", centro.Numero);
        Assert.Contains("aguardando o Nível 1", centro.Tempo);
        // o que virou compra no mês fecha a lista
        var compra = Assert.Single(radar, x => x.Tipo == AlertaDoAlmoxarifado.RotaDeCompra);
        Assert.Equal("virou PR-2026-000099", compra.Tempo);
        // a liberada hoje está dentro do prazo e não aparece
        Assert.DoesNotContain(radar, x => x.Numero == "MR-4");
        // a fila do centro: a mais antiga e a cor
        var parede = await w.Parede.ObterAsync();
        Assert.Equal("MR-2", parede.MaisAntigaAguardandoNumero);
        Assert.Equal(NoDaEsteira.Critico, parede.GargaloAguardando);
    }

    [Fact]
    public async Task O_recorte_por_unidade_entra_pela_empresa_do_centro_e_o_centro_sem_empresa_fica_na_geral()
    {
        var w = await CenarioAsync();
        var pb = await w.Parede.ObterAsync("Trino PB");
        var ba = await w.Parede.ObterAsync("Trino BA");
        var geral = await w.Parede.ObterAsync();

        Assert.Equal("Trino PB", pb.Unidade);
        Assert.Equal(3, pb.SolicitadasNoMes);          // MR-1, MR-3, MR-4
        Assert.Equal(1, pb.ForaDoPrazo);
        Assert.All(pb.Radar, x => Assert.NotEqual("MR-2", x.Numero));
        Assert.DoesNotContain(pb.Radar, x => x.Tipo == AlertaDoAlmoxarifado.RotaDeCompra);
        Assert.Single(pb.PorCentro);
        Assert.StartsWith("PB-001", pb.PorCentro[0].Rotulo);

        Assert.Equal(1, ba.SolicitadasNoMes);          // só a MR-2
        Assert.Equal(0, ba.ForaDoPrazo);
        Assert.Single(ba.Radar, x => x.Tipo == AlertaDoAlmoxarifado.AguardandoOCentro);

        // a do centro sem empresa conta só na visão geral: pô-la numa unidade seria inventar o dado
        Assert.Equal(5, geral.SolicitadasNoMes);
        Assert.Single(geral.Radar, x => x.Tipo == AlertaDoAlmoxarifado.RotaDeCompra);
    }

    [Fact]
    public async Task Sem_atendimento_medido_a_parede_mostra_nulo_e_nao_zero()
    {
        var w = await BuildAsync();
        var parede = await w.Parede.ObterAsync();
        Assert.Null(parede.AtendidasNoPrazoPct);
        Assert.Null(parede.HorasMediaAprovacao);
        Assert.Equal(0, parede.ForaDoPrazo);
        Assert.Empty(parede.Radar);
        Assert.Equal(NoDaEsteira.Normal, parede.GargaloAguardando);
    }
}
