using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Compliance;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Insights;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

public class InsightsServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Hoje = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private static (AppDbContext db, InsightsService svc) Build()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new AppDbContext(options);
        var clock = new FixedTimeProvider(Hoje);
        return (db, new InsightsService(db, new ComplianceService(db, clock), clock));
    }

    private static PurchaseOrder Po(string number, string supplier, params PurchaseOrderItem[] items)
    {
        var po = new PurchaseOrder
        {
            Number = number, SupplierId = Guid.NewGuid(), SupplierName = supplier,
            Status = PurchaseOrderStatus.Issued, IssuedBy = Guid.NewGuid(), IssuedByLabel = "Comprador",
            CreatedAt = Hoje.AddDays(-10), UpdatedAt = Hoje.AddDays(-10),
        };
        foreach (var i in items) { i.OrderId = po.Id; po.Items.Add(i); }
        po.TotalValue = po.Items.Sum(i => (i.UnitPrice ?? 0) * i.Quantity);
        return po;
    }

    [Fact]
    public async Task Sobrepreco_acima_de_20_por_cento_do_ultimo_preco_vira_insight()
    {
        var (db, svc) = Build();
        db.PurchaseOrders.Add(Po("OC-1", "Fornecedor X",
            new PurchaseOrderItem { Description = "Tinta", Quantity = 10, UnitPrice = 130, LastPaidUnitPrice = 100, CreatedAt = Hoje },
            new PurchaseOrderItem { Description = "Rolo", Quantity = 5, UnitPrice = 105, LastPaidUnitPrice = 100, CreatedAt = Hoje }));
        await db.SaveChangesAsync();

        var achados = await svc.FindInsightsAsync(6);

        var sobre = achados.Where(i => i.Code == "INS-01").ToList();
        Assert.Single(sobre);                                     // 30% entra; 5% não
        Assert.Contains("Tinta", sobre[0].Title);
        Assert.Contains("OC-1", sobre[0].Evidence);
    }

    [Fact]
    public async Task Fracionamento_soma_SCs_pequenas_acima_do_limite_do_centro()
    {
        var (db, svc) = Build();
        db.CostCenters.Add(new CostCenter
        {
            Code = "CC-01", Name = "Centro", Active = true, Level2ValueLimit = 1000,
            CreatedAt = Hoje, UpdatedAt = Hoje,
        });
        for (var i = 0; i < 3; i++)
            db.Requisitions.Add(new PurchaseRequisition
            {
                Number = $"PR-{i}", CostCenter = "CC-01", Status = RequisitionStatus.Approved,
                Justification = "x", RequesterId = Guid.NewGuid(), RequesterLabel = "Ana",
                CreatedAt = Hoje.AddDays(-20 + i * 5), UpdatedAt = Hoje,
                Items = { new RequisitionItem { Description = "Item", Quantity = 1, UnitOfMeasure = "UN",
                    EstimatedUnitPrice = 600, CreatedAt = Hoje } },
            });
        await db.SaveChangesAsync();

        var achados = await svc.FindInsightsAsync(6);

        var frac = achados.Where(i => i.Code == "INS-02").ToList();
        Assert.Single(frac);                                      // 3 × 600 = 1800 > 1000
        Assert.Contains("CC-01", frac[0].Title);
    }

    [Fact]
    public async Task Tres_urgencias_no_periodo_viram_insight_de_emergenciais()
    {
        var (db, svc) = Build();
        for (var i = 0; i < 3; i++)
            db.Requisitions.Add(new PurchaseRequisition
            {
                Number = $"PR-U{i}", CostCenter = "CC-02", Status = RequisitionStatus.Approved,
                Priority = "URGENT", Justification = "x",
                RequesterId = Guid.NewGuid(), RequesterLabel = "Ana",
                CreatedAt = Hoje.AddDays(-i * 10), UpdatedAt = Hoje,
            });
        await db.SaveChangesAsync();

        var achados = await svc.FindInsightsAsync(6);

        Assert.Single(achados.Where(i => i.Code == "INS-03"));
    }

    // ---- INTEL-A/B: providência em cada achado, e três regras novas ----------

    [Fact]
    public async Task Todo_achado_diz_o_que_fazer()
    {
        // descrever um problema sem dizer o que fazer devolve o trabalho a quem lê
        var (db, svc) = Build();
        db.PurchaseOrders.Add(Po("PO-1", "Alfa",
            new PurchaseOrderItem { Description = "Luva", Quantity = 10, UnitPrice = 16m, LastPaidUnitPrice = 10m }));
        await db.SaveChangesAsync();

        var achados = await svc.FindInsightsAsync(6);
        Assert.NotEmpty(achados);
        Assert.All(achados, a => Assert.False(string.IsNullOrWhiteSpace(a.Action)));
        // e o destino, quando existe, é um id de tela que a interface sabe resolver
        Assert.All(achados.Where(a => a.View is not null),
            a => Assert.Contains(a.View, new[] { "buy-orders", "triage", "suppliers", "quotations", "scorecard" }));
    }

    [Fact]
    public async Task Fechar_sem_oc_do_erp_vira_achado_quando_deixa_de_ser_excecao()
    {
        // PO-BR-011: a observação é a exceção prevista. O que não pode é virar rotina
        var (db, svc) = Build();
        for (var i = 1; i <= 3; i++)
        {
            var po = Po($"PO-{i}", "Alfa", new PurchaseOrderItem { Description = "Item", Quantity = 1, UnitPrice = 100m });
            po.NoErpReason = "compra emergencial de balcão";
            db.PurchaseOrders.Add(po);
        }
        await db.SaveChangesAsync();

        var achado = Assert.Single(await svc.FindInsightsAsync(6), a => a.Code == "INS-05");
        Assert.Contains("3 compras", achado.Title);
        Assert.Equal("buy-orders", achado.View);

        // com número do ERP, não há achado nenhum: o caminho normal não acusa
        var (db2, svc2) = Build();
        for (var i = 1; i <= 5; i++)
        {
            var po = Po($"PO-{i}", "Alfa", new PurchaseOrderItem { Description = "Item", Quantity = 1, UnitPrice = 100m });
            po.ErpNumber = $"OC-{i}";
            db2.PurchaseOrders.Add(po);
        }
        await db2.SaveChangesAsync();
        Assert.DoesNotContain(await svc2.FindInsightsAsync(6), a => a.Code == "INS-05");
    }

    [Fact]
    public async Task Fornecedor_que_atrasa_de_novo_vira_padrao_e_nao_caso_isolado()
    {
        var (db, svc) = Build();
        for (var i = 1; i <= 3; i++)
        {
            var po = Po($"PO-{i}", "Beta", new PurchaseOrderItem { Description = "Item", Quantity = 1, UnitPrice = 50m });
            po.PromisedDate = new DateOnly(2026, 8, 1);
            po.DeliveryCompletedAt = Hoje;            // entregue depois do prometido
            db.PurchaseOrders.Add(po);
        }
        await db.SaveChangesAsync();

        var achado = Assert.Single(await svc.FindInsightsAsync(6), a => a.Code == "INS-06");
        Assert.Contains("Beta", achado.Title);
        Assert.Equal("scorecard", achado.View);
    }

    [Fact]
    public async Task Decidir_com_proposta_unica_aparece_como_compra_sem_comparacao()
    {
        var (db, svc) = Build();
        var q = new Quotation
        {
            Number = "RFQ-2026-000001", Kind = QuotationKind.Purchase, Status = QuotationStatus.PoIssued,
            CostCenter = "BAH-001", CreatedAt = Hoje.AddDays(-5), WinnerProposalId = Guid.NewGuid(),
        };
        db.Quotations.Add(q);
        await db.SaveChangesAsync();

        var achado = Assert.Single(await svc.FindInsightsAsync(6), a => a.Code == "INS-07");
        Assert.Contains("RFQ-2026-000001", achado.Evidence);
        Assert.Equal("quotations", achado.View);
    }

    /// <summary>
    /// INTEL-C: o achado de severidade alta é o que precisa chegar a quem decide
    /// sem depender de alguém abrir a tela de Insights. Este teste fixa a régua
    /// que a Central de Avisos usa para escolher o que promover.
    /// </summary>
    [Fact]
    public async Task Achado_grave_se_distingue_do_que_e_so_para_acompanhar()
    {
        var (db, svc) = Build();
        // sobrepreço de 60% é alta; de 25%, média
        db.PurchaseOrders.Add(Po("PO-1", "Alfa",
            new PurchaseOrderItem { Description = "Luva", Quantity = 1, UnitPrice = 16m, LastPaidUnitPrice = 10m }));
        db.PurchaseOrders.Add(Po("PO-2", "Beta",
            new PurchaseOrderItem { Description = "Bota", Quantity = 1, UnitPrice = 12.5m, LastPaidUnitPrice = 10m }));
        await db.SaveChangesAsync();

        var achados = await svc.FindInsightsAsync(6);
        var graves = achados.Where(a => a.Severity == "alta").ToList();
        Assert.Single(graves);
        Assert.Contains("Luva", graves[0].Title);
        // e o grave vem primeiro: a ordenação é por severidade
        Assert.Equal("alta", achados[0].Severity);
    }
}
