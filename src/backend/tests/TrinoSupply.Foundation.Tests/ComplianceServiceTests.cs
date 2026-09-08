using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Compliance;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

public class ComplianceServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeNumbers : IPrNumberGenerator
    {
        private int _next;
        public Task<string> NextAsync(CancellationToken ct = default) =>
            Task.FromResult($"PR-2026-{++_next:000000}");
    }

    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana Solicitante", Roles.Requester);
    private static readonly Actor Bruno = new(Guid.NewGuid(), "Bruno Aprovador", Roles.Approver);
    private static readonly Actor Carla = new(Guid.NewGuid(), "Carla Compradora", Roles.PurchasingOfficer);
    private static readonly Actor Gustavo = new(Guid.NewGuid(), "Gustavo Gestor", Roles.SupplyManager);
    private static readonly Actor Diana = new(Guid.NewGuid(), "Diana Diretora", Roles.Director);

    private sealed record World(
        QuotationService Rfq, RequisitionService Prs, SupplierService Sup, ComplianceService Cp,
        AppDbContext Db, Supplier Alfa, Supplier Beta, FixedTimeProvider Clock);

    private static readonly DateTimeOffset Hoje = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private static async Task<World> BuildAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new AppDbContext(options);
        var clock = new FixedTimeProvider(Hoje);
        var rfq = new QuotationService(db, clock);
        var prs = new RequisitionService(db, new FakeNumbers(), new CatalogService(db, clock), clock);
        var sup = new SupplierService(db, clock);
        var cp = new ComplianceService(db, clock);
        var (alfa, _) = await sup.CreateAsync(Carla.Id, "Alfa LTDA", "Alfa", "12345678000190", null, null);
        var (beta, _) = await sup.CreateAsync(Carla.Id, "Beta LTDA", "Beta", "98765432000110", null, null);
        // fornecedor novo nasce PROSPECT: participar da cotação é livre, vencer exige
        // homologação (SUP-ERR-030). O cenário destes testes é o do fornecedor já
        // homologado — quem cuida do caminho do prospect é o teste próprio dele.
        await sup.SetHomologationAsync(alfa!.Id, SupplierHomologation.Homologado);
        await sup.SetHomologationAsync(beta!.Id, SupplierHomologation.Homologado);
        return new World(rfq, prs, sup, cp, db, alfa!, beta!, clock);
    }

    private static async Task<PurchaseRequisition> ScAprovadaAsync(
        World w, string priority = "NORMAL", DateOnly? neededBy = null,
        string? urgencyReason = null, string? urgencyImpact = null)
    {
        var (pr, e) = await w.Prs.CreateAsync(Ana, "Compra de teste", "CC-01", priority, neededBy,
            [new ItemInput("Item de teste", 2, "UN", 100, null)],
            header: new RequisitionService.ScHeaderInput(null, null, null, null, urgencyReason, urgencyImpact));
        Assert.Null(e);
        var (_, se) = await w.Prs.SubmitAsync(Ana, pr!.Id);
        Assert.Null(se);
        var (_, ae) = await w.Prs.ApproveAsync(Bruno, pr.Id, null);
        Assert.Null(ae);
        return pr;
    }

    private static ProposalInput Proposta(Quotation q, decimal preco) =>
        new(10, "28 dias", 0, null, null,
            q.Items.Select(i => new ProposalItemInput(i.Id, preco, null)).ToList());

    /// <summary>Conduz o processo até a escolha do vencedor com os fornecedores informados.</summary>
    private static async Task<Quotation> AteVencedorAsync(World w, PurchaseRequisition pr, params Supplier[] fornecedores)
    {
        var (q, e) = await w.Rfq.CreateFromPrAsync(Carla, pr.Id, QuotationKind.Purchase, null, null);
        Assert.Null(e);
        await w.Rfq.InviteSuppliersAsync(Carla, q!.Id, fornecedores.Select(f => f.Id).ToList());
        foreach (var (f, idx) in fornecedores.Select((f, i) => (f, i)))
            await w.Rfq.SubmitProposalAsync(q.Id, f.Id, Proposta(q, 100 + idx * 10), "PORTAL", f.TradeName!);
        await w.Rfq.CloseForAnalysisAsync(Carla, q.Id);
        var atual = (await w.Rfq.GetAsync(q.Id))!;
        var vencedora = atual.Proposals.OrderBy(p => p.TotalValue).First();
        var (sel, se) = await w.Rfq.SelectWinnerAsync(Carla, q.Id, vencedora.Id, "Preço", "Menor preço.");
        Assert.Null(se);
        return sel!;
    }

    [Fact]
    public async Task Processo_limpo_recebe_score_100()
    {
        var w = await BuildAsync();
        var pr = await ScAprovadaAsync(w, neededBy: DateOnly.FromDateTime(Hoje.UtcDateTime).AddDays(30));
        await AteVencedorAsync(w, pr, w.Alfa, w.Beta);

        var report = await w.Cp.ReportAsync();

        var row = Assert.Single(report.Items);
        Assert.Equal(100, row.Score);
        Assert.Empty(row.Penalties);
        Assert.Equal(1, report.FullCompliance);
    }

    [Fact]
    public async Task Uma_proposta_e_urgencia_penalizam_25_e_20()
    {
        var w = await BuildAsync();
        var pr = await ScAprovadaAsync(w, priority: "URGENT",
            urgencyReason: "Máquina parada", urgencyImpact: "Linha inteira sem produzir");
        await AteVencedorAsync(w, pr, w.Alfa);   // um único concorrente

        var report = await w.Cp.ReportAsync();

        var row = Assert.Single(report.Items);
        Assert.Equal(55, row.Score);
        Assert.Contains(row.Penalties, p => p.Code == "CP-01" && p.Points == 25);
        Assert.Contains(row.Penalties, p => p.Code == "CP-02" && p.Points == 20);
    }

    [Fact]
    public async Task Processo_aberto_depois_da_necessidade_penaliza_20()
    {
        var w = await BuildAsync();
        var pr = await ScAprovadaAsync(w, neededBy: DateOnly.FromDateTime(Hoje.UtcDateTime).AddDays(2));
        w.Clock.Now = Hoje.AddDays(5);   // a demanda ficou parada e a necessidade passou
        await AteVencedorAsync(w, pr, w.Alfa, w.Beta);

        var report = await w.Cp.ReportAsync();

        var row = Assert.Single(report.Items);
        Assert.Equal(80, row.Score);
        var pena = Assert.Single(row.Penalties);
        Assert.Equal("CP-04", pena.Code);
        Assert.Contains(pr.Number, pena.Evidence);
    }

    [Fact]
    public async Task Processo_acima_do_limite_de_alcada_do_centro_penaliza_10()
    {
        var w = await BuildAsync();
        w.Db.CostCenters.Add(new CostCenter
        {
            Code = "CC-01", Name = "Centro limitado", Active = true,
            Level2ValueLimit = 150m,   // proposta vencedora fecha em 200 (2 × 100)
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await w.Db.SaveChangesAsync();
        var pr = await ScAprovadaAsync(w, neededBy: DateOnly.FromDateTime(Hoje.UtcDateTime).AddDays(30));
        await AteVencedorAsync(w, pr, w.Alfa, w.Beta);

        var report = await w.Cp.ReportAsync();

        var row = Assert.Single(report.Items);
        Assert.Equal(90, row.Score);
        var pena = Assert.Single(row.Penalties);
        Assert.Equal("CP-05", pena.Code);
        Assert.Contains("Nível 2", pena.Evidence);
    }

    [Fact]
    public async Task Vencedor_que_perdeu_a_homologacao_penaliza_30_sem_bloquear_nada()
    {
        var w = await BuildAsync();
        var pr = await ScAprovadaAsync(w, neededBy: DateOnly.FromDateTime(Hoje.UtcDateTime).AddDays(30));
        var q = await AteVencedorAsync(w, pr, w.Alfa, w.Beta);

        // depois da escolha, o cadastro do vencedor regride para RESTRITO
        var vencedor = await w.Db.Suppliers.SingleAsync(s => s.Id == q.WinnerSupplierId);
        vencedor.HomologationStatus = SupplierHomologation.Restrito;
        await w.Db.SaveChangesAsync();

        var report = await w.Cp.ReportAsync();

        var row = Assert.Single(report.Items);
        Assert.Equal(70, row.Score);
        var pena = Assert.Single(row.Penalties);
        Assert.Equal("CP-03", pena.Code);

        // mede, não bloqueia: as alçadas seguem normalmente
        var (mgr, e) = await w.Rfq.ManagerDecisionAsync(Gustavo, q.Id, "APROVAR", null);
        Assert.Null(e);
        Assert.Equal(QuotationStatus.AwaitingDirector, mgr!.Status);
    }
}
