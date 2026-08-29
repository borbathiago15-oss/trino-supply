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
}
