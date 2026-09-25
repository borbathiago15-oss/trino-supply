using Microsoft.EntityFrameworkCore;
using TrinoSupply.Foundation.Api.Catalog;
using TrinoSupply.Foundation.Api.Domain;
using TrinoSupply.Foundation.Api.Infrastructure;
using TrinoSupply.Foundation.Api.Procurement;

namespace TrinoSupply.Foundation.Tests;

/// <summary>
/// A 2ª alçada da compra que a própria área de compras pediu (decisão da empresa, 2026-09):
/// a compra do Gestor de Suprimentos não tem Nível 2, e a do comprador tem o gestor dele no
/// lugar da diretoria. Tudo o mais continua terminando na diretoria.
/// </summary>
public class AlcadaDoCompradorTests
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

    private static readonly Actor Ana = new(Guid.NewGuid(), "Ana Solicitante", Roles.Requester);
    private static readonly Actor Bruno = new(Guid.NewGuid(), "Bruno Aprovador", Roles.Approver);
    private static readonly Actor Carla = new(Guid.NewGuid(), "Carla Compradora", Roles.PurchasingOfficer);
    private static readonly Actor Caio = new(Guid.NewGuid(), "Caio Comprador", Roles.PurchasingOfficer);
    private static readonly Actor Gustavo = new(Guid.NewGuid(), "Gustavo Gestor", Roles.SupplyManager);
    private static readonly Actor Diana = new(Guid.NewGuid(), "Diana Diretora", Roles.Director);

    private sealed record World(
        QuotationService Rfq, RequisitionService Prs, AppDbContext Db, Supplier Alfa, Supplier Beta);

    /// <summary>Carla tem Gustavo como gestor responsável; Caio não tem ninguém.</summary>
    private static async Task<World> BuildAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(new ItensDaCotacaoNoCatalogo()).Options;
        var db = new AppDbContext(options);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero));

        static User Usuario(Actor a, Guid? gestor = null) => new()
        {
            Id = a.Id, Email = $"{a.Id:N}@trino.dev", Name = a.Label, Role = a.Role,
            PasswordHash = "x", SupplyManagerId = gestor,
        };
        db.Users.AddRange(Usuario(Ana), Usuario(Bruno), Usuario(Carla, gestor: Gustavo.Id),
            Usuario(Caio), Usuario(Gustavo), Usuario(Diana));
        await db.SaveChangesAsync();

        var sup = new SupplierService(db, clock);
        var (alfa, _) = await sup.CreateAsync(Carla.Id, "Alfa LTDA", "Alfa", "12345678000190", null, "81 3333-1000");
        var (beta, _) = await sup.CreateAsync(Carla.Id, "Beta LTDA", "Beta", "98765432000110", null, "81 3333-2000");
        await sup.SetHomologationAsync(alfa!.Id, SupplierHomologation.Homologado);
        await sup.SetHomologationAsync(beta!.Id, SupplierHomologation.Homologado);

        return new World(new QuotationService(db, clock),
            new RequisitionService(db, new FakeNumbers(), new CatalogService(db, clock), clock),
            db, alfa!, beta!);
    }

    /// <summary>SC de <paramref name="quemPede"/>, cotada e escolhida por <paramref name="comprador"/>.</summary>
    private static async Task<Quotation> AteAEscolhaAsync(World w, Actor quemPede, Actor comprador)
    {
        var (pr, ePr) = await w.Prs.CreateAsync(quemPede, "Ferramentas de manutenção", "CC-01", "NORMAL", null,
            [new ItemInput("Martelete MRP 900", 1, "UN", 900, null),
             new ItemInput("Pé de cabra", 1, "UN", 70, null)]);
        Assert.Null(ePr);
        await w.Prs.SubmitAsync(quemPede, pr!.Id);
        await w.Prs.ApproveAsync(Bruno, pr.Id, null);

        var (q, eQ) = await w.Rfq.CreateFromPrAsync(comprador, pr.Id, QuotationKind.Purchase, null, null);
        Assert.Null(eQ);
        await w.Rfq.InviteSuppliersAsync(comprador, q!.Id, [w.Alfa.Id, w.Beta.Id]);
        static ProposalInput Proposta(Quotation q, decimal a, decimal b) =>
            new(15, "28 dias", 0, null, null,
                q.Items.OrderBy(i => i.Sequence).Zip(new[] { a, b })
                    .Select(x => new ProposalItemInput(x.First.Id, x.Second, null)).ToList());
        await w.Rfq.SubmitProposalAsync(q.Id, w.Alfa.Id, Proposta(q, 857.65m, 65.08m), "PORTAL", "Alfa");
        await w.Rfq.SubmitProposalAsync(q.Id, w.Beta.Id, Proposta(q, 950m, 80m), "PORTAL", "Beta");
        var (fechada, eF) = await w.Rfq.CloseForAnalysisAsync(comprador, q.Id);
        Assert.Null(eF);
        var vencedora = fechada!.Proposals.First(p => p.SupplierId == w.Alfa.Id);
        var (escolhida, eS) = await w.Rfq.SelectWinnerAsync(
            comprador, fechada.Id, vencedora.Id, "Preço, Prazo", "Menor preço com prazo aceitável.");
        Assert.Null(eS);
        return escolhida!;
    }

    // ---- compra do próprio Gestor de Suprimentos: sem Nível 2 -----------------

    [Fact]
    public async Task Compra_do_gestor_aprova_no_nivel_1_e_ja_fica_pronta_para_a_OC()
    {
        var w = await BuildAsync();
        var q = await AteAEscolhaAsync(w, quemPede: Gustavo, comprador: Gustavo);

        var (aprovada, erro) = await w.Rfq.ManagerDecisionAsync(Gustavo, q.Id, "APROVAR", null);

        Assert.Null(erro);
        Assert.Equal(QuotationStatus.ApprovedForIssue, aprovada!.Status);
        // ninguém assinou como diretoria, e é por esse nulo que todo o resto reconhece a dispensa
        Assert.Null(aprovada.DirectorApprovedBy);
        Assert.Null(aprovada.DirectorApprovedAt);
        Assert.Equal(Gustavo.Id, aprovada.ManagerApprovedBy);
        // o pedido nasce aqui, como nasceria no Nível 2
        Assert.NotNull(aprovada.PurchaseOrderId);
        // e as SCs de origem ficam autorizadas com preço
        Assert.All(await w.Db.Requisitions.ToListAsync(), r => Assert.Equal(RequisitionStatus.Approved, r.Status));
    }

    [Fact]
    public async Task Gestor_que_apenas_cota_a_SC_de_outro_nao_dispensa_o_nivel_2()
    {
        var w = await BuildAsync();
        // a SC é da Ana: a compra não é do gestor, ainda que ele conduza o processo
        var q = await AteAEscolhaAsync(w, quemPede: Ana, comprador: Gustavo);

        var (aprovada, erro) = await w.Rfq.ManagerDecisionAsync(Gustavo, q.Id, "APROVAR", null);

        Assert.Null(erro);
        Assert.Equal(QuotationStatus.AwaitingDirector, aprovada!.Status);
    }

    // ---- compra do comprador: o Nível 2 é o gestor dele -----------------------

    [Fact]
    public async Task Compra_do_comprador_vai_ao_gestor_responsavel_e_nao_a_diretoria()
    {
        var w = await BuildAsync();
        var q = await AteAEscolhaAsync(w, quemPede: Carla, comprador: Carla);
        await w.Rfq.ManagerDecisionAsync(Carla, q.Id, "APROVAR", null);

        // a diretoria não decide esta: ela é da hierarquia de compras
        var (_, recusa) = await w.Rfq.DirectorDecisionAsync(Diana, q.Id, "APROVAR", null);
        Assert.Equal("RFQ-ERR-032", recusa!.Code);
        Assert.Contains("Gustavo Gestor", recusa.Message);

        var (aprovada, erro) = await w.Rfq.DirectorDecisionAsync(Gustavo, q.Id, "APROVAR", null);
        Assert.Null(erro);
        Assert.Equal(QuotationStatus.ApprovedForIssue, aprovada!.Status);
        Assert.Equal(Gustavo.Id, aprovada.DirectorApprovedBy);
    }

    [Fact]
    public async Task A_fila_da_central_mostra_ao_gestor_a_compra_dos_compradores_dele()
    {
        var w = await BuildAsync();
        var q = await AteAEscolhaAsync(w, quemPede: Carla, comprador: Carla);
        await w.Rfq.ManagerDecisionAsync(Carla, q.Id, "APROVAR", null);

        // a fila e a decisão perguntam à mesma régua: o que aparece é o que se decide
        var doGestor = await w.Rfq.PendingApprovalsAsync(Roles.SupplyManager, Gustavo.Id);
        Assert.Contains(doGestor, x => x.Id == q.Id);

        var daDiretora = await w.Rfq.PendingApprovalsAsync(Roles.Director, Diana.Id);
        Assert.DoesNotContain(daDiretora, x => x.Id == q.Id);
    }

    [Fact]
    public async Task Comprador_sem_gestor_cadastrado_continua_indo_a_diretoria()
    {
        var w = await BuildAsync();
        // Caio não tem responsável: a régua de sempre vale, e a fila não trava
        var q = await AteAEscolhaAsync(w, quemPede: Caio, comprador: Caio);
        await w.Rfq.ManagerDecisionAsync(Caio, q.Id, "APROVAR", null);

        var (aprovada, erro) = await w.Rfq.DirectorDecisionAsync(Diana, q.Id, "APROVAR", null);
        Assert.Null(erro);
        Assert.Equal(QuotationStatus.ApprovedForIssue, aprovada!.Status);
    }

    [Fact]
    public async Task O_gestor_nao_herda_a_segunda_alcada_de_processo_que_nao_e_dos_compradores_dele()
    {
        var w = await BuildAsync();
        // SC da Ana, cotada pela Carla: a 2ª alçada é da diretoria, não do gestor da Carla
        var q = await AteAEscolhaAsync(w, quemPede: Ana, comprador: Carla);
        await w.Rfq.ManagerDecisionAsync(Carla, q.Id, "APROVAR", null);

        var (_, recusa) = await w.Rfq.DirectorDecisionAsync(Gustavo, q.Id, "APROVAR", null);
        Assert.Equal("RFQ-ERR-032", recusa!.Code);

        var (aprovada, erro) = await w.Rfq.DirectorDecisionAsync(Diana, q.Id, "APROVAR", null);
        Assert.Null(erro);
        Assert.Equal(QuotationStatus.ApprovedForIssue, aprovada!.Status);
    }
}
