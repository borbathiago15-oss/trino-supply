using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Analytics;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Materials;
using TrinoSupply.Foundation.Api.Users;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// A solicitação de material no Dashboard: o período conta o criado e o atendido; a pendência é
/// de agora; o filtro por família entra pelo item; o prazo é o da fila do almoxarifado.
/// </summary>
public class AnalyticsDeMaterialTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Hoje = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Beto = Guid.NewGuid();

    private sealed record World(AnalyticsDeMaterialService Svc, AppDbContext Db, CatalogItem Luva, CatalogItem Papel);

    private static async Task<World> BuildAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new AppDbContext(options);
        var clock = new FixedTimeProvider(Hoje);
        var catalog = new CatalogService(db, clock);
        var (luva, _) = await catalog.CreateAsync(Ana, "EPI-001", "Luva nitrílica", "EPI", "PAR", 12m);
        var (papel, _) = await catalog.CreateAsync(Ana, "ESC-001", "Papel A4", "ESCRITORIO", "PC", 25m);
        db.CostCenters.AddRange(
            new CostCenter { Code = "BAH-001", Name = "Obra Bahia", Active = true, CreatedAt = Hoje, UpdatedAt = Hoje },
            new CostCenter { Code = "BAH-002", Name = "Obra Salvador", Active = true, CreatedAt = Hoje, UpdatedAt = Hoje });
        await db.SaveChangesAsync();
        return new World(new AnalyticsDeMaterialService(db, clock, new PrazoDeAtendimentoService(db, clock)), db, luva!, papel!);
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
                    : status == MaterialRequisitionStatus.PurchaseRoute ? MaterialItemStatus.PurchaseRoute : MaterialItemStatus.Pending,
            });
        return mr;
    }

    private static async Task<World> CenarioAsync()
    {
        var w = await BuildAsync();
        // atendida em 2 dias depois da aprovação (4h depois de criada)
        var m1 = Mr("MR-1", "BAH-001", Ana, "Ana", Dia(10, 1), MaterialRequisitionStatus.Fulfilled, (w.Luva, 10, 10));
        m1.ApprovedAt = Dia(10, 1, 13); m1.FulfilledAt = Dia(10, 3, 13);
        // espera a aprovação do centro
        var m2 = Mr("MR-2", "BAH-002", Beto, "Beto", Dia(10, 2), MaterialRequisitionStatus.Submitted, (w.Papel, 5, 0));
        // na fila do estoque há 4 dias: o prazo padrão é 2, está estourada
        var m3 = Mr("MR-3", "BAH-001", Ana, "Ana", Dia(10, 3), MaterialRequisitionStatus.Approved, (w.Luva, 2, 0));
        m3.ApprovedAt = Dia(10, 3);
        // da janela anterior (26/09): só conta na base da variação
        var m4 = Mr("MR-4", "BAH-001", Beto, "Beto", Dia(9, 26), MaterialRequisitionStatus.Fulfilled, (w.Papel, 1, 1));
        m4.ApprovedAt = Dia(9, 26); m4.FulfilledAt = Dia(9, 27);
        // recusada no período
        var m5 = Mr("MR-5", "BAH-002", Ana, "Ana", Dia(10, 4), MaterialRequisitionStatus.Rejected, (w.Luva, 3, 0));
        m5.ApprovedAt = Dia(10, 4, 10);
        w.Db.MaterialRequisitions.AddRange(m1, m2, m3, m4, m5);
        await w.Db.SaveChangesAsync();
        return w;
    }

    [Fact]
    public async Task O_periodo_conta_o_criado_e_o_atendido_e_a_pendencia_e_de_agora()
    {
        var w = await CenarioAsync();
        var r = await w.Svc.MaterialAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7), null, null, null);

        Assert.Equal(4, r.Kpis.Requested);          // MR-1, MR-2, MR-3, MR-5
        Assert.Equal(1, r.Kpis.RequestedPrev);      // MR-4, nos 7 dias anteriores
        Assert.Equal(1, r.Kpis.AwaitingApproval);   // MR-2
        Assert.Equal(1, r.Kpis.InWarehouseQueue);   // MR-3
        Assert.Equal(1, r.Kpis.Fulfilled);          // MR-1
        Assert.Equal(1, r.Kpis.Rejected);           // MR-5
        Assert.Equal(20m, r.Kpis.RequestedQty);     // 10 + 5 + 2 + 3
        Assert.Equal(10m, r.Kpis.DeliveredQty);

        Assert.Equal(2.0, r.Kpis.AvgApprovalHours); // MR-1 4h e MR-3 0h; a recusada fica fora
        Assert.Equal(2.0, r.Kpis.AvgFulfillDays);
        Assert.Equal(2.2, r.Kpis.AvgTotalDays);     // 2 dias e 4 horas
        Assert.Equal(100.0, r.Kpis.SlaMetPercent);  // 2 dias dentro do padrão de 2
        Assert.Equal(1, r.Kpis.SlaMeasured);
        Assert.Equal(1, r.Kpis.SlaBreachedOpen);    // MR-3, há 4 dias na fila

        Assert.Single(r.Months);
        Assert.Equal(("2026-10", 4, 1, 1), (r.Months[0].Month, r.Months[0].Requested, r.Months[0].Fulfilled, r.Months[0].Rejected));
    }

    [Fact]
    public async Task Os_rankings_saem_das_criadas_no_periodo_por_centro_familia_produto_e_solicitante()
    {
        var w = await CenarioAsync();
        var r = await w.Svc.MaterialAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7), null, null, null);

        var centro = r.ByCostCenter[0];
        Assert.Equal(("BAH-001 — Obra Bahia", "BAH-001", 2, 12m), (centro.Label, centro.Key, centro.Count, centro.Qty));
        Assert.Equal("EPI", r.ByFamily[0].Label);
        Assert.Equal(15m, r.ByFamily[0].Qty);            // 10 + 2 + 3
        Assert.Equal(3, r.ByFamily[0].Count);
        Assert.StartsWith("[EPI-001]", r.ByProduct[0].Label);
        Assert.Equal(w.Luva.Id.ToString(), r.ByProduct[0].Key);
        Assert.Equal("Ana", r.ByRequester[0].Label);     // 3 solicitações
        Assert.Equal(3, r.ByRequester[0].Count);

        var epi = Assert.Single(r.SlaByFamily, f => f.Family == "EPI");
        Assert.Equal((2, 1, 1, 2.0), (epi.MaxDays, epi.Measured, epi.Met, epi.AvgDays));

        Assert.Contains(r.FilterOptions.CostCenters, c => c.Code == "BAH-002" && c.Name == "Obra Salvador");
        Assert.Equal(AnalyticsDeMaterialService.Definicoes, r.Indicators);
    }

    [Fact]
    public async Task O_filtro_por_familia_entra_pelo_item_e_o_de_centro_pela_solicitacao()
    {
        var w = await CenarioAsync();
        var de = new DateOnly(2026, 10, 1); var ate = new DateOnly(2026, 10, 7);

        var escritorio = await w.Svc.MaterialAsync(de, ate, null, "escritorio", null);
        Assert.Equal(1, escritorio.Kpis.Requested);           // só MR-2
        Assert.Equal(1, escritorio.Kpis.AwaitingApproval);
        Assert.Equal(0, escritorio.Kpis.InWarehouseQueue);
        Assert.Single(escritorio.ByProduct);
        Assert.StartsWith("[ESC-001]", escritorio.ByProduct[0].Label);

        var salvador = await w.Svc.MaterialAsync(de, ate, "bah-002", null, null);
        Assert.Equal(2, salvador.Kpis.Requested);             // MR-2 e MR-5, sem caixa
        Assert.Equal(0, salvador.Kpis.Fulfilled);

        var luva = await w.Svc.MaterialAsync(de, ate, null, null, "luva");
        Assert.Equal(3, luva.Kpis.Requested);                 // MR-1, MR-3, MR-5
        Assert.Equal(1, luva.Kpis.SlaBreachedOpen);
    }

    [Fact]
    public async Task Sem_atendimento_no_periodo_as_medias_e_o_prazo_sao_nulos_e_nao_zero()
    {
        var w = await CenarioAsync();
        var r = await w.Svc.MaterialAsync(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 7), null, null, null);
        Assert.Equal(0, r.Kpis.Requested);
        Assert.Null(r.Kpis.AvgFulfillDays);
        Assert.Null(r.Kpis.AvgTotalDays);
        Assert.Null(r.Kpis.SlaMetPercent);
        // a pendência continua sendo de agora: as duas abertas aparecem mesmo fora do período
        Assert.Equal(1, r.Kpis.AwaitingApproval);
        Assert.Equal(1, r.Kpis.InWarehouseQueue);
        Assert.Equal(1, r.Kpis.SlaBreachedOpen);
    }

    [Fact]
    public void Quem_le_a_compra_e_o_almoxarife_leem_o_material()
    {
        Assert.True(AnalyticsDeMaterialService.CanView(Roles.Director));
        Assert.True(AnalyticsDeMaterialService.CanView(Roles.WarehouseOperator));
        Assert.False(AnalyticsDeMaterialService.CanView(Roles.Requester));
    }
}
